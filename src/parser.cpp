#include "ollang.h"
#include <stdexcept>
#include <variant>
#include <sstream>
#include <memory>

namespace {
    bool isStringNode(const std::shared_ptr<Node>& node) {
        return dynamic_cast<StringNode*>(node.get()) != nullptr;
    }

    std::string getStringNodeValue(const std::shared_ptr<Node>& node) {
        if (auto strNode = dynamic_cast<StringNode*>(node.get())) {
            return strNode->value;
        }
        return "";
    }
}

const Token* Parser::current() const {
    return pos < tokens.size() ? &tokens[pos] : nullptr;
}

void Parser::advance() {
    if (pos < tokens.size()) pos++;
}

bool Parser::match(const std::string& type, const std::string& value) {
    const Token* tk = current();
    if (!tk) return false;
    if (tk->type != type) return false;
    if (value.empty()) return true;
    if (auto s = std::get_if<std::string>(&tk->value)) return *s == value;
    return false;
}

Token Parser::expect(const std::string& type, const std::string& value) {
    const Token* tk = current();
    if (!tk) {
        std::stringstream ss;
        ss << "Expected " << (value.empty() ? type : "'" + value + "'") << " but reached end of file";
        throw std::runtime_error(ss.str());
    }

    if (tk->type != type) {
        std::stringstream ss;
        ss << "Expected " << (value.empty() ? type : "'" + value + "'") << " but got '" << tk->type << "'";
        if (std::holds_alternative<std::string>(tk->value))
            ss << " '" << std::get<std::string>(tk->value) << "'";
            ss << " at line " << tk->line << ":" << tk->col;
        throw std::runtime_error(ss.str());
    }

    if (!value.empty() && std::holds_alternative<std::string>(tk->value) &&
        std::get<std::string>(tk->value) != value) {
        std::stringstream ss;
        ss << "Expected '" << value << "' but got '" << std::get<std::string>(tk->value) << "'" << " at line " << tk->line << ":" << tk->col;
        throw std::runtime_error(ss.str());
    }

    Token r = *tk;
    advance();
    return r;
}

Parser::Parser(const std::vector<Token>& t) : tokens(t), pos(0) {}

std::shared_ptr<ProgramNode> Parser::parse() {
    auto p = std::make_shared<ProgramNode>();
    while (current()) p->body.push_back(parseStatement());
    return p;
}

std::shared_ptr<Node> Parser::parseStatement() {
    if (match("keyword", "func")) return parseFunc();
    if (match("keyword", "if")) return parseIf();
    if (match("keyword", "while")) return parseWhile();
    if (match("keyword", "for")) return parseFor();
    if (match("keyword", "return")) return parseReturn();
    if (match("keyword", "alloc")) return parseAlloc();
    if (match("keyword", "free")) return parseFree();
    if (match("keyword", "read")) return parseReadMem();
    if (match("keyword", "write")) return parseWriteMem();
    if (match("keyword", "syscall")) return parseSyscall();
    if (match("keyword", "try")) return parseTryCatch();
    if (match("keyword", "async")) return parseAsyncFunc();
    if (match("keyword", "throw")) return parseThrow();
    if (match("keyword", "import")) {
        advance();
        std::string module = std::get<std::string>(expect("string").value);
        expect("punctuation", ";");
        return std::make_shared<ImportNode>(module);
    }
    if (match("keyword", "ImportDLL")) return parseImportDLL();
    if (match("keyword", "namespace")) return parseNamespace();
    return parseExpressionStatement();
}

std::shared_ptr<Node> Parser::parseFunc() {
    expect("keyword", "func");
    std::string name = std::get<std::string>(expect("identifier").value);
    expect("punctuation", "(");
    std::vector<std::string> params;
    if (!match("punctuation", ")")) {
        params.push_back(std::get<std::string>(expect("identifier").value));
        while (match("punctuation", ",")) {
            advance();
            params.push_back(std::get<std::string>(expect("identifier").value));
        }
    }
    expect("punctuation", ")");
    auto n = std::make_shared<FunctionDefNode>();
    n->name = name;
    n->params = params;
    n->body = parseBlock();
    return n;
}

std::shared_ptr<Node> Parser::parseIf() {
    expect("keyword", "if");
    auto n = std::make_shared<IfNode>();
    n->condition = parseExpression();
    n->thenBranch = parseBlock();
    n->elseBranch.clear();
    if (match("keyword", "else")) {
        advance();
        n->elseBranch = parseBlock();
    }
    return n;
}

std::shared_ptr<Node> Parser::parseWhile() {
    expect("keyword", "while");
    auto n = std::make_shared<WhileNode>();
    n->condition = parseExpression();
    n->body = parseBlock();
    return n;
}

std::shared_ptr<Node> Parser::parseFor() {
    expect("keyword", "for");
    std::string var = std::get<std::string>(expect("identifier").value);
    expect("keyword", "in");
    auto n = std::make_shared<ForNode>();
    n->var = var;
    n->iterable = parseExpression();
    n->body = parseBlock();
    return n;
}

std::shared_ptr<Node> Parser::parseReturn() {
    expect("keyword", "return");
    auto n = std::make_shared<ReturnNode>();
    n->value = parseExpression();
    return n;
}

std::shared_ptr<Node> Parser::parseSyscall() {
    expect("keyword", "syscall");
    auto n = std::make_shared<SyscallNode>();
    n->syscallNum = parseExpression();
    expect("punctuation", "(");
    if (!match("punctuation", ")")) {
        n->arguments.push_back(parseExpression());
        while (match("punctuation", ",")) {
            advance();
            n->arguments.push_back(parseExpression());
        }
    }
    expect("punctuation", ")");
    return n;
}

std::shared_ptr<Node> Parser::parseAlloc() {
    expect("keyword", "alloc");
    expect("punctuation", "(");
    auto n = std::make_shared<AllocNode>();
    n->size = parseExpression();
    expect("punctuation", ")");
    return n;
}

std::shared_ptr<Node> Parser::parseFree() {
    expect("keyword", "free");
    expect("punctuation", "(");
    auto n = std::make_shared<FreeNode>();
    n->ptr = parseExpression();
    expect("punctuation", ")");
    return n;
}

std::shared_ptr<Node> Parser::parseReadMem() {
    expect("keyword", "read");
    expect("punctuation", "(");
    auto n = std::make_shared<ReadMemNode>();
    n->ptr = parseExpression();
    expect("punctuation", ",");
    n->offset = parseExpression();
    expect("punctuation", ",");
    n->type = std::get<std::string>(expect("string").value);
    expect("punctuation", ")");
    return n;
}

std::shared_ptr<Node> Parser::parseWriteMem() {
    expect("keyword", "write");
    expect("punctuation", "(");
    auto n = std::make_shared<WriteMemNode>();
    n->ptr = parseExpression();
    expect("punctuation", ",");
    n->offset = parseExpression();
    expect("punctuation", ",");
    n->value = parseExpression();
    expect("punctuation", ",");
    n->type = std::get<std::string>(expect("string").value);
    expect("punctuation", ")");
    return n;
}

std::shared_ptr<Node> Parser::parseTryCatch() {
    expect("keyword", "try");
    auto n = std::make_shared<TryCatchNode>();
    n->tryBody = parseBlock();
    expect("keyword", "catch");
    expect("punctuation", "(");
    n->catchVar = std::get<std::string>(expect("identifier").value);
    expect("punctuation", ")");
    n->catchBody = parseBlock();
    return n;
}

std::shared_ptr<Node> Parser::parseAsyncFunc() {
    expect("keyword", "async");
    expect("keyword", "func");
    std::string name = std::get<std::string>(expect("identifier").value);
    expect("punctuation", "(");
    std::vector<std::string> params;
    if (!match("punctuation", ")")) {
        params.push_back(std::get<std::string>(expect("identifier").value));
        while (match("punctuation", ",")) {
            advance();
            params.push_back(std::get<std::string>(expect("identifier").value));
        }
    }
    expect("punctuation", ")");
    auto n = std::make_shared<AsyncFunctionDefNode>();
    n->name = name;
    n->params = params;
    n->body = parseBlock();
    return n;
}

std::shared_ptr<Node> Parser::parseAwait() {
    expect("keyword", "await");
    auto n = std::make_shared<AwaitNode>();
    n->expression = parseExpression();
    return n;
}

std::shared_ptr<Node> Parser::parseThrow() {
    expect("keyword", "throw");
    auto n = std::make_shared<ThrowNode>();
    n->value = parseExpression();
    return n;
}

std::shared_ptr<Node> Parser::parseImportDLL() {
    expect("keyword", "ImportDLL");
    expect("punctuation", "(");

    auto dllPathExpr = parseExpression();
    expect("punctuation", ",");
    auto funcNameExpr = parseExpression();

    std::string alias;
    if (match("punctuation", ",")) {
        advance();
        auto aliasExpr = parseExpression();
        if (isStringNode(aliasExpr)) {
            alias = getStringNodeValue(aliasExpr);
        }
    }

    expect("punctuation", ")");

    std::string dllPath = getStringNodeValue(dllPathExpr);
    std::string funcName = getStringNodeValue(funcNameExpr);

    if (dllPath.empty()) {
        throw std::runtime_error("ImportDLL first argument must be a string");
    }

    if (funcName.empty()) {
        throw std::runtime_error("ImportDLL second argument must be a string");
    }

    return std::make_shared<ImportDLLNode>(dllPath, funcName, alias.empty() ? funcName : alias);
}

std::shared_ptr<Node> Parser::parseNamespace() {
    expect("keyword", "namespace");
    std::string name = std::get<std::string>(expect("identifier").value);
    expect("punctuation", "{");
    auto n = std::make_shared<NamespaceNode>();
    n->name = name;
    while (!match("punctuation", "}")) {
        n->body.push_back(parseStatement());
    }
    expect("punctuation", "}");
    return n;
}

std::vector<std::shared_ptr<Node>> Parser::parseBlock() {
    expect("punctuation", "{");
    std::vector<std::shared_ptr<Node>> stmts;
    while (!match("punctuation", "}")) stmts.push_back(parseStatement());
    expect("punctuation", "}");
    return stmts;
}

std::shared_ptr<Node> Parser::parseExpressionStatement() {
    auto n = std::make_shared<ExpressionStatementNode>();
    n->expression = parseExpression();
    return n;
}

std::shared_ptr<Node> Parser::parseExpression() {
    if (match("keyword", "await")) return parseAwait();
    return parseAssignment();
}

std::shared_ptr<Node> Parser::parseAssignment() {
    auto left = parseLogical();
    if (match("operator", "=")) {
        advance();
        auto value = parseAssignment();
        if (auto* ident = dynamic_cast<IdentifierNode*>(left.get())) {
            auto n = std::make_shared<AssignmentNode>();
            n->name = ident->name;
            n->value = value;
            return n;
        }
        else if (auto* idx = dynamic_cast<IndexNode*>(left.get())) {
            auto n = std::make_shared<IndexAssignNode>();
            n->object = idx->object;
            n->index = idx->index;
            n->value = value;
            return n;
        }
        else if (auto* dot = dynamic_cast<DotNode*>(left.get())) {
            auto n = std::make_shared<DotAssignNode>();
            n->object = dot->object;
            n->member = dot->member;
            n->value = value;
            return n;
        }
        throw std::runtime_error("Invalid assignment target");
    }
    return left;
}

std::shared_ptr<Node> Parser::parseLogical() {
    auto left = parseComparison();
    while (match("operator", "&&") || match("operator", "||") ||
        match("operator", "&") || match("operator", "|") || match("operator", "^")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<BinaryOpNode>();
        n->op = op;
        n->left = left;
        n->right = parseComparison();
        left = n;
    }
    return left;
}

std::shared_ptr<Node> Parser::parseComparison() {
    auto left = parseTerm();
    while (match("operator", "<") || match("operator", ">") ||
        match("operator", "<=") || match("operator", ">=") ||
        match("operator", "==") || match("operator", "!=")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<BinaryOpNode>();
        n->op = op;
        n->left = left;
        n->right = parseTerm();
        left = n;
    }
    return left;
}

std::shared_ptr<Node> Parser::parseTerm() {
    auto left = parseFactor();
    while (match("operator", "+") || match("operator", "-") ||
        match("operator", "<<") || match("operator", ">>")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<BinaryOpNode>();
        n->op = op;
        n->left = left;
        n->right = parseFactor();
        left = n;
    }
    return left;
}

std::shared_ptr<Node> Parser::parseFactor() {
    auto left = parseUnary();
    while (match("operator", "*") || match("operator", "/") ||
        match("operator", "%")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<BinaryOpNode>();
        n->op = op;
        n->left = left;
        n->right = parseUnary();
        left = n;
    }
    return left;
}

std::shared_ptr<Node> Parser::parseUnary() {
    if (match("operator", "!") || match("operator", "-") || match("operator", "~")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<UnaryOpNode>();
        n->op = op;
        n->operand = parseUnary();
        return n;
    }
    return parseCall();
}

std::shared_ptr<Node> Parser::parseCall() {
    auto left = parsePrimary();
    while (true) {
        if (match("punctuation", "(")) {
            auto n = std::make_shared<CallNode>();
            n->callee = left;
            advance();
            if (!match("punctuation", ")")) {
                n->arguments.push_back(parseExpression());
                while (match("punctuation", ",")) {
                    advance();
                    n->arguments.push_back(parseExpression());
                }
            }
            expect("punctuation", ")");
            left = n;
        }
        else if (match("punctuation", "[")) {
            advance();
            auto n = std::make_shared<IndexNode>();
            n->object = left;
            n->index = parseExpression();
            expect("punctuation", "]");
            left = n;
        }
        else if (match("punctuation", ".")) {
            advance();
            auto n = std::make_shared<DotNode>();
            n->object = left;
            n->member = std::get<std::string>(expect("identifier").value);
            left = n;
        }
        else {
            break;
        }
    }
    return left;
}

std::shared_ptr<Node> Parser::parsePrimary() {
    if (match("number")) {
        double val = std::get<double>(current()->value);
        advance();
        return std::make_shared<NumberNode>(val);
    }
    if (match("string")) {
        std::string val = std::get<std::string>(current()->value);
        advance();
        return std::make_shared<StringNode>(val);
    }
    if (match("keyword", "true")) {
        advance();
        return std::make_shared<BooleanNode>(true);
    }
    if (match("keyword", "false")) {
        advance();
        return std::make_shared<BooleanNode>(false);
    }
    if (match("keyword", "null")) {
        advance();
        return std::make_shared<NullNode>();
    }
    if (match("identifier")) {
        std::string name = std::get<std::string>(current()->value);
        advance();
        return std::make_shared<IdentifierNode>(name);
    }
    if (match("punctuation", "(")) {
        advance();
        auto expr = parseExpression();
        expect("punctuation", ")");
        return expr;
    }
    if (match("punctuation", "[")) {
        advance();

        if (match("punctuation", "]")) {
            advance();
            return std::make_shared<ArrayNode>();
        }

        auto first = parseExpression();

        if (match("keyword", "for")) {
            expect("keyword", "for");
            std::string var = std::get<std::string>(expect("identifier").value);
            expect("keyword", "in");
            auto iter = parseExpression();
            std::shared_ptr<Node> cond = nullptr;
            if (match("keyword", "if")) {
                advance();
                cond = parseExpression();
            }
            expect("punctuation", "]");

            auto n = std::make_shared<ListComprehensionNode>();
            n->expression = first;
            n->var = var;
            n->iterable = iter;
            n->condition = cond;
            return n;
        }

        auto n = std::make_shared<ArrayNode>();
        n->elements.push_back(first);

        while (match("punctuation", ",")) {
            advance();
            n->elements.push_back(parseExpression());
        }

        expect("punctuation", "]");
        return n;
    }
    if (match("punctuation", "{")) {
        advance();
        auto n = std::make_shared<DictNode>();
        if (!match("punctuation", "}")) {
            std::string key = std::get<std::string>(expect("string").value);
            expect("operator", ":");
            auto value = parseExpression();
            n->entries[key] = value;
            while (match("punctuation", ",")) {
                advance();
                key = std::get<std::string>(expect("string").value);
                expect("operator", ":");
                value = parseExpression();
                n->entries[key] = value;
            }
        }
        expect("punctuation", "}");
        return n;
    }
    const Token* tk = current();
    std::stringstream ss;
    ss << "Unexpected token";
    if (tk) {
        ss << " '" << tk->type << "'";
        if (std::holds_alternative<std::string>(tk->value)) {
            ss << " '" << std::get<std::string>(tk->value) << "'";
        }
        ss << " at line " << tk->line << ":" << tk->col;
    }
    throw std::runtime_error(ss.str());
}

std::shared_ptr<Node> Parser::parseListComprehension() {
    expect("punctuation", "[");
    auto expr = parseExpression();
    expect("keyword", "for");
    std::string var = std::get<std::string>(expect("identifier").value);
    expect("keyword", "in");
    auto iter = parseExpression();
    std::shared_ptr<Node> cond = nullptr;
    if (match("keyword", "if")) {
        advance();
        cond = parseExpression();
    }
    expect("punctuation", "]");
    auto n = std::make_shared<ListComprehensionNode>();
    n->expression = expr;
    n->var = var;
    n->iterable = iter;
    n->condition = cond;
    return n;
}
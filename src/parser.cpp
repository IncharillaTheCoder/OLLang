#include "ollang.h"
#include <stdexcept>
#include <variant>
#include <sstream>
#include <memory>
#include <algorithm>
#include <unordered_set>

// Helper functions for string node checking
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

    bool isNumberNode(const std::shared_ptr<Node>& node) {
        return dynamic_cast<NumberNode*>(node.get()) != nullptr;
    }

    double getNumberNodeValue(const std::shared_ptr<Node>& node) {
        if (auto numNode = dynamic_cast<NumberNode*>(node.get())) {
            return numNode->value;
        }
        return 0.0;
    }
}

// Get current token
const Token* Parser::current() const {
    return pos < tokens.size() ? &tokens[pos] : nullptr;
}

// Move to next token
void Parser::advance() {
    if (pos < tokens.size()) pos++;
}

// Check if current token matches expected type/value
bool Parser::match(const std::string& type, const std::string& value) {
    const Token* tk = current();
    if (!tk) return false;
    if (tk->type != type) return false;
    if (value.empty()) return true;
    if (auto s = std::get_if<std::string>(&tk->value)) return *s == value;
    return false;
}

// Expect a specific token, throw error if not found
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

// Main parse function - creates program node
std::shared_ptr<ProgramNode> Parser::parse() {
    auto p = std::make_shared<ProgramNode>();
    while (current()) p->body.push_back(parseStatement());
    return p;
}

// Parse different statement types
std::shared_ptr<Node> Parser::parseStatement() {
    // Check for all possible statement keywords
    if (match("keyword", "func")) {
        if (match("keyword", "func") || match("keyword", "async")) {
            // Handle async func or other combinations
            if (match("keyword", "async")) {
                advance(); // skip async
                return parseAsyncFunc();
            }
        }
        return parseFunc();
    }
    if (match("keyword", "async")) {
        advance();
        if (match("keyword", "func")) {
            return parseAsyncFunc();
        }
        throw std::runtime_error("Expected 'func' after 'async'");
    }
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
    if (match("keyword", "throw")) return parseThrow();
    if (match("keyword", "import")) {
        advance();
        std::string module = std::get<std::string>(expect("string").value);
        expect("punctuation", ";");
        return std::make_shared<ImportNode>(module);
    }
    if (match("keyword", "ImportDLL")) return parseImportDLL();
    if (match("keyword", "namespace")) return parseNamespace();
    if (match("keyword", "await")) return parseAwait();
    if (match("keyword", "process")) return parseProcessStatement();
    if (match("keyword", "inject")) return parseInjectStatement();
    if (match("keyword", "hook")) return parseHookStatement();
    if (match("keyword", "scan")) return parseScanStatement();
    if (match("keyword", "window")) return parseWindowStatement();
    if (match("keyword", "thread")) return parseThreadStatement();

    return parseExpressionStatement();
}

// Parse function definition
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

// Parse if statement
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

// Parse while loop
std::shared_ptr<Node> Parser::parseWhile() {
    expect("keyword", "while");
    auto n = std::make_shared<WhileNode>();
    n->condition = parseExpression();
    n->body = parseBlock();
    return n;
}

// Parse for loop
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

// Parse return statement
std::shared_ptr<Node> Parser::parseReturn() {
    expect("keyword", "return");
    auto n = std::make_shared<ReturnNode>();
    if (!match("punctuation", ";") && !match("punctuation", "}")) {
        n->value = parseExpression();
    }
    return n;
}

// Parse syscall statement
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
    expect("punctuation", ";");
    return n;
}

// Parse memory allocation
std::shared_ptr<Node> Parser::parseAlloc() {
    expect("keyword", "alloc");
    expect("punctuation", "(");
    auto n = std::make_shared<AllocNode>();
    n->size = parseExpression();
    expect("punctuation", ")");
    expect("punctuation", ";");
    return n;
}

// Parse memory free
std::shared_ptr<Node> Parser::parseFree() {
    expect("keyword", "free");
    expect("punctuation", "(");
    auto n = std::make_shared<FreeNode>();
    n->ptr = parseExpression();
    expect("punctuation", ")");
    expect("punctuation", ";");
    return n;
}

// Parse memory read
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
    expect("punctuation", ";");
    return n;
}

// Parse memory write
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
    expect("punctuation", ";");
    return n;
}

// Parse try-catch block
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

// Parse async function
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

// Parse await expression
std::shared_ptr<Node> Parser::parseAwait() {
    expect("keyword", "await");
    auto n = std::make_shared<AwaitNode>();
    n->expression = parseExpression();
    return n;
}

// Parse throw statement
std::shared_ptr<Node> Parser::parseThrow() {
    expect("keyword", "throw");
    auto n = std::make_shared<ThrowNode>();
    n->value = parseExpression();
    expect("punctuation", ";");
    return n;
}

// Parse DLL import
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
    expect("punctuation", ";");

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

// Parse namespace
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

// Parse list comprehension (used in parsePrimary)
std::shared_ptr<Node> Parser::parseListComprehension() {
    // This is called from parsePrimary when we detect [expr for var in iterable]
    auto expr = parseExpression();
    expect("keyword", "for");
    std::string var = std::get<std::string>(expect("identifier").value);
    expect("keyword", "in");
    auto iterable = parseExpression();

    std::shared_ptr<Node> condition = nullptr;
    if (match("keyword", "if")) {
        advance();
        condition = parseExpression();
    }

    expect("punctuation", "]");

    auto n = std::make_shared<ListComprehensionNode>();
    n->expression = expr;
    n->var = var;
    n->iterable = iterable;
    n->condition = condition;
    return n;
}

// Parse process manipulation statement
std::shared_ptr<Node> Parser::parseProcessStatement() {
    expect("keyword", "process");
    std::string action = std::get<std::string>(expect("identifier").value);

    if (action == "find") {
        expect("punctuation", "(");
        auto processName = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("findProcess");
        callNode->arguments.push_back(processName);
        return callNode;
    }
    else if (action == "open") {
        expect("punctuation", "(");
        auto pid = parseExpression();
        expect("punctuation", ",");
        auto access = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("openProcess");
        callNode->arguments.push_back(pid);
        callNode->arguments.push_back(access);
        return callNode;
    }
    else if (action == "close") {
        expect("punctuation", "(");
        auto handle = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("closeHandle");
        callNode->arguments.push_back(handle);
        return callNode;
    }

    throw std::runtime_error("Unknown process action: " + action);
}

// Parse DLL injection
std::shared_ptr<Node> Parser::parseInjectStatement() {
    expect("keyword", "inject");
    expect("punctuation", "(");
    auto pid = parseExpression();
    expect("punctuation", ",");
    auto dllPath = parseExpression();
    expect("punctuation", ")");

    auto callNode = std::make_shared<CallNode>();
    callNode->callee = std::make_shared<IdentifierNode>("injectDLL");
    callNode->arguments.push_back(pid);
    callNode->arguments.push_back(dllPath);
    return callNode;
}

// Parse function hooking
std::shared_ptr<Node> Parser::parseHookStatement() {
    expect("keyword", "hook");
    std::string type = std::get<std::string>(expect("identifier").value);
    expect("punctuation", "(");
    auto target = parseExpression();
    expect("punctuation", ",");
    auto destination = parseExpression();
    expect("punctuation", ")");

    auto callNode = std::make_shared<CallNode>();

    if (type == "jmp") {
        callNode->callee = std::make_shared<IdentifierNode>("writeJmp");
    }
    else if (type == "call") {
        callNode->callee = std::make_shared<IdentifierNode>("writeCall");
    }
    else {
        throw std::runtime_error("Unknown hook type: " + type);
    }

    callNode->arguments.push_back(target);
    callNode->arguments.push_back(destination);
    return callNode;
}

// Parse memory scanning
std::shared_ptr<Node> Parser::parseScanStatement() {
    expect("keyword", "scan");
    std::string type = std::get<std::string>(expect("identifier").value);
    expect("punctuation", "(");

    if (type == "memory") {
        auto process = parseExpression();
        expect("punctuation", ",");
        auto start = parseExpression();
        expect("punctuation", ",");
        auto size = parseExpression();
        expect("punctuation", ",");
        auto pattern = parseExpression();
        expect("punctuation", ",");
        auto patternLen = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("scanMemory");
        callNode->arguments.push_back(process);
        callNode->arguments.push_back(start);
        callNode->arguments.push_back(size);
        callNode->arguments.push_back(pattern);
        callNode->arguments.push_back(patternLen);
        return callNode;
    }

    throw std::runtime_error("Unknown scan type: " + type);
}

// Parse window manipulation
std::shared_ptr<Node> Parser::parseWindowStatement() {
    expect("keyword", "window");
    std::string action = std::get<std::string>(expect("identifier").value);

    if (action == "find") {
        expect("punctuation", "(");
        auto className = parseExpression();
        expect("punctuation", ",");
        auto windowName = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("findWindow");
        callNode->arguments.push_back(className);
        callNode->arguments.push_back(windowName);
        return callNode;
    }
    else if (action == "getpid") {
        expect("punctuation", "(");
        auto hwnd = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("getWindowProcessId");
        callNode->arguments.push_back(hwnd);
        return callNode;
    }

    throw std::runtime_error("Unknown window action: " + action);
}

// Parse thread manipulation
std::shared_ptr<Node> Parser::parseThreadStatement() {
    expect("keyword", "thread");
    std::string action = std::get<std::string>(expect("identifier").value);

    if (action == "create") {
        expect("punctuation", "(");
        auto startAddress = parseExpression();
        expect("punctuation", ",");
        auto parameter = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("createThread");
        callNode->arguments.push_back(startAddress);
        callNode->arguments.push_back(parameter);
        return callNode;
    }
    else if (action == "suspend") {
        expect("punctuation", "(");
        auto threadHandle = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("suspendThread");
        callNode->arguments.push_back(threadHandle);
        return callNode;
    }
    else if (action == "resume") {
        expect("punctuation", "(");
        auto threadHandle = parseExpression();
        expect("punctuation", ")");

        auto callNode = std::make_shared<CallNode>();
        callNode->callee = std::make_shared<IdentifierNode>("resumeThread");
        callNode->arguments.push_back(threadHandle);
        return callNode;
    }

    throw std::runtime_error("Unknown thread action: " + action);
}

// Parse block of statements
std::vector<std::shared_ptr<Node>> Parser::parseBlock() {
    expect("punctuation", "{");
    std::vector<std::shared_ptr<Node>> stmts;
    while (!match("punctuation", "}")) stmts.push_back(parseStatement());
    expect("punctuation", "}");
    return stmts;
}

// Parse expression statement
std::shared_ptr<Node> Parser::parseExpressionStatement() {
    auto expr = parseExpression();
    if (!match("punctuation", "}") && !match("keyword", "else") && !match("keyword", "catch")) {
        expect("punctuation", ";");
    }

    auto n = std::make_shared<ExpressionStatementNode>();
    n->expression = expr;
    return n;
}

// Parse expression (top level)
std::shared_ptr<Node> Parser::parseExpression() {
    return parseAssignment();
}

// Parse assignment
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

// Parse logical operators (&&, ||, &, |, ^)
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

// Parse comparison operators (==, !=, <, >, <=, >=)
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

// Parse term operators (+, -, <<, >>)
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

// Parse factor operators (*, /, %, **)
std::shared_ptr<Node> Parser::parseFactor() {
    auto left = parsePower();
    while (match("operator", "*") || match("operator", "/") ||
        match("operator", "%")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<BinaryOpNode>();
        n->op = op;
        n->left = left;
        n->right = parsePower();
        left = n;
    }
    return left;
}

// Parse power operator (**)
std::shared_ptr<Node> Parser::parsePower() {
    auto left = parseUnary();
    while (match("operator", "**")) {
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

// Parse unary operators (!, -, ~)
std::shared_ptr<Node> Parser::parseUnary() {
    if (match("operator", "!") || match("operator", "-") || match("operator", "~")) {
        std::string op = std::get<std::string>(current()->value);
        advance();
        auto n = std::make_shared<UnaryOpNode>();
        n->op = op;
        n->operand = parseUnary();
        return n;
    }

    // Handle await expression
    if (match("keyword", "await")) {
        return parseAwait();
    }

    return parseCall();
}

// Parse function calls, indexing, and member access
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

    // Handle keywords that should be treated as function identifiers
    if (match("keyword")) {
        std::string name = std::get<std::string>(current()->value);

        // Check if this keyword should be treated as an identifier (function call)
        std::unordered_set<std::string> functionKeywords = {
            "checkapp", "getapppid", "waitforapp", "waitforappclose",
            "killapp", "startapp", "getAvailableMemory", "UUID",
            "httpGet", "httpPost", "httpPut", "httpDelete",
            "base64Encode", "base64Decode", "base64EncodeFile",
            "base64DecodeFile", "base64UrlEncode", "base64UrlDecode"
        };

        if (functionKeywords.count(name)) {
            advance();
            return std::make_shared<IdentifierNode>(name);
        }

        // If it's not a function keyword, let it fall through to error
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
            // List comprehension
            return parseListComprehension();
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
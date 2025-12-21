#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
#include <memory>
#include <map>
#include <unordered_map>
#include <functional>
#include <variant>
#include <cstdint>
#include <future>
#include "ollang_c.h"

struct Value;
struct Node;
using ValuePtr = std::shared_ptr<Value>;

struct Value {
    virtual ~Value() = default;
    virtual std::string toString() const = 0;
    virtual bool isTruthy() const { return true; }
};

struct NumberValue : Value {
    double value;
    NumberValue(double v = 0);
    std::string toString() const override;
    bool isTruthy() const override;
};

struct StringValue : Value {
    std::string value;
    StringValue(const std::string& v = "");
    std::string toString() const override;
    bool isTruthy() const override;
};

struct BooleanValue : Value {
    bool value;
    BooleanValue(bool v = false);
    std::string toString() const override;
    bool isTruthy() const override;
};

struct NullValue : Value {
    std::string toString() const override;
    bool isTruthy() const override;
};

struct ArrayValue : Value {
    std::vector<ValuePtr> elements;
    std::string toString() const override;
};

struct DictValue : Value {
    std::unordered_map<std::string, ValuePtr> items;
    std::string toString() const override;
};

struct FunctionValue : Value {
    std::string name;
    std::vector<std::string> params;
    std::vector<std::shared_ptr<Node>> body;
    std::map<std::string, ValuePtr> closure;
    FunctionValue(const std::string& n, const std::vector<std::string>& p,
        const std::vector<std::shared_ptr<Node>>& b);
    std::string toString() const override;
};

struct BuiltinValue : Value {
    using BuiltinFunc = std::function<ValuePtr(const std::vector<ValuePtr>&)>;
    BuiltinFunc func;
    std::string name;
    BuiltinValue(const std::string& n, BuiltinFunc f);
    std::string toString() const override;
};

struct PointerValue : Value {
    void* ptr;
    size_t size;
    bool owned;
    PointerValue(void* p = nullptr, size_t s = 0, bool o = false);
    std::string toString() const override;
    bool isTruthy() const override;
    ~PointerValue();
};

struct PromiseValue;
struct AsyncFunctionValue;

struct DLLFunctionValue;

struct Token {
    std::string type;
    std::variant<std::string, double> value;
    int line, col;
    Token(const std::string& t, const std::variant<std::string, double>& v, int l, int c);
};

struct Node {
    virtual ~Node() = default;
    virtual ValuePtr eval(class Interpreter& interpreter) = 0;
};

struct ProgramNode : Node {
    std::vector<std::shared_ptr<Node>> body;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ExpressionStatementNode : Node {
    std::shared_ptr<Node> expression;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct NumberNode : Node {
    double value;
    NumberNode(double v);
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct StringNode : Node {
    std::string value;
    StringNode(const std::string& v);
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct BooleanNode : Node {
    bool value;
    BooleanNode(bool v);
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct IdentifierNode : Node {
    std::string name;
    IdentifierNode(const std::string& n);
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ImportNode : Node {
    std::string module;
    ImportNode(const std::string& m) : module(m) {}
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct AssignmentNode : Node {
    std::string name;
    std::shared_ptr<Node> value;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct BinaryOpNode : Node {
    std::string op;
    std::shared_ptr<Node> left, right;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct UnaryOpNode : Node {
    std::string op;
    std::shared_ptr<Node> operand;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct FunctionDefNode : Node {
    std::string name;
    std::vector<std::string> params;
    std::vector<std::shared_ptr<Node>> body;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct CallNode : Node {
    std::shared_ptr<Node> callee;
    std::vector<std::shared_ptr<Node>> arguments;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct IfNode : Node {
    std::shared_ptr<Node> condition;
    std::vector<std::shared_ptr<Node>> thenBranch, elseBranch;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct WhileNode : Node {
    std::shared_ptr<Node> condition;
    std::vector<std::shared_ptr<Node>> body;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ForNode : Node {
    std::string var;
    std::shared_ptr<Node> iterable;
    std::vector<std::shared_ptr<Node>> body;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ReturnNode : Node {
    std::shared_ptr<Node> value;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ArrayNode : Node {
    std::vector<std::shared_ptr<Node>> elements;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct DictNode : Node {
    std::unordered_map<std::string, std::shared_ptr<Node>> entries;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct IndexNode : Node {
    std::shared_ptr<Node> object, index;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct DotNode : Node {
    std::shared_ptr<Node> object;
    std::string member;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct IndexAssignNode : Node {
    std::shared_ptr<Node> object, index, value;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct DotAssignNode : Node {
    std::shared_ptr<Node> object;
    std::string member;
    std::shared_ptr<Node> value;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct NullNode : Node {
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct SyscallNode : Node {
    std::shared_ptr<Node> syscallNum;
    std::vector<std::shared_ptr<Node>> arguments;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct AllocNode : Node {
    std::shared_ptr<Node> size;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct FreeNode : Node {
    std::shared_ptr<Node> ptr;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ReadMemNode : Node {
    std::shared_ptr<Node> ptr;
    std::shared_ptr<Node> offset;
    std::string type;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct WriteMemNode : Node {
    std::shared_ptr<Node> ptr;
    std::shared_ptr<Node> offset;
    std::shared_ptr<Node> value;
    std::string type;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct TryCatchNode : Node {
    std::vector<std::shared_ptr<Node>> tryBody;
    std::string catchVar;
    std::vector<std::shared_ptr<Node>> catchBody;
    TryCatchNode() = default;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct AwaitNode : Node {
    std::shared_ptr<Node> expression;
    AwaitNode() = default;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct AsyncFunctionDefNode : Node {
    std::string name;
    std::vector<std::string> params;
    std::vector<std::shared_ptr<Node>> body;
    AsyncFunctionDefNode() = default;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ThrowNode : Node {
    std::shared_ptr<Node> value;
    ThrowNode() = default;
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ImportDLLNode : Node {
    std::string dllPath;
    std::string functionName;
    std::string alias;
    ImportDLLNode(const std::string& path, const std::string& func, const std::string& a) : dllPath(path), functionName(func), alias(a) {}
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct NamespaceNode : Node {
    std::string name;
    std::vector<std::shared_ptr<Node>> body;
    NamespaceNode() = default;
    NamespaceNode(const std::string& n, const std::vector<std::shared_ptr<Node>>& b)
        : name(n), body(b) {
    }
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct ListComprehensionNode : Node {
    std::string var;
    std::shared_ptr<Node> iterable;
    std::shared_ptr<Node> condition;
    std::shared_ptr<Node> expression;
    ListComprehensionNode() = default;
    ListComprehensionNode(const std::string& v, std::shared_ptr<Node> i,
        std::shared_ptr<Node> c, std::shared_ptr<Node> e)
        : var(v), iterable(i), condition(c), expression(e) {
    }
    ValuePtr eval(class Interpreter& interpreter) override;
};

struct HttpRequest {
    std::string method; // GET, POST, PUT, DELETE
    std::string url;
	std::map < std::string, std::string> headers;
	std::string body;
    int timeout_ms;
	bool verify_ssl;
};

struct HttpResponse {
	int status_code;
	std::map<std::string, std::string> headers;
    std::string body;
    std::string error;
    bool success;
};

struct HttpClient {
public: 
	static HttpResponse request(const HttpRequest& req);
	static std::string download(const std::string& url, const std::string& save_path);
    static std::string post_json(const std::string& url, const std::string& json_data);
};


class Lexer {
    std::string source;
    size_t pos = 0;
    int line = 1, col = 1;

    char current() const;
    char peek(size_t offset = 1) const;
    void advance();
    void skipWhitespace();
    void skipComment();
    double readNumber();
    std::string readString();
    std::string readIdentifier();

public:
    Lexer(const std::string& src);
    std::vector<Token> tokenize();
};

class Parser {
    std::vector<Token> tokens;
    size_t pos = 0;

    const Token* current() const;
    void advance();
    bool match(const std::string& type, const std::string& value = "");
    Token expect(const std::string& type, const std::string& value = "");

public:
    Parser(const std::vector<Token>& t);
    std::shared_ptr<ProgramNode> parse();

private:
    std::shared_ptr<Node> parseStatement();
    std::shared_ptr<Node> parseFunc();
    std::shared_ptr<Node> parseIf();
    std::shared_ptr<Node> parseWhile();
    std::shared_ptr<Node> parseFor();
    std::shared_ptr<Node> parseReturn();
    std::shared_ptr<Node> parseSyscall();
    std::shared_ptr<Node> parseAlloc();
    std::shared_ptr<Node> parseFree();
    std::shared_ptr<Node> parseReadMem();
    std::shared_ptr<Node> parseWriteMem();
    std::vector<std::shared_ptr<Node>> parseBlock();
    std::shared_ptr<Node> parseExpressionStatement();
    std::shared_ptr<Node> parseExpression();
    std::shared_ptr<Node> parseAssignment();
    std::shared_ptr<Node> parseLogical();
    std::shared_ptr<Node> parseComparison();
    std::shared_ptr<Node> parseTerm();
    std::shared_ptr<Node> parseFactor();
    std::shared_ptr<Node> parsePower();
    std::shared_ptr<Node> parseUnary();
    std::shared_ptr<Node> parseCall();
    std::shared_ptr<Node> parsePrimary();
    std::shared_ptr<Node> parseTryCatch();
    std::shared_ptr<Node> parseAsyncFunc();
    std::shared_ptr<Node> parseAwait();
    std::shared_ptr<Node> parseThrow();
    std::shared_ptr<Node> parseImportDLL();
    std::shared_ptr<Node> parseNamespace();
    std::shared_ptr<Node> parseListComprehension();

    std::shared_ptr<Node> parseProcessStatement();
    std::shared_ptr<Node> parseInjectStatement();
    std::shared_ptr<Node> parseHookStatement();
    std::shared_ptr<Node> parseScanStatement();
    std::shared_ptr<Node> parseWindowStatement();
    std::shared_ptr<Node> parseThreadStatement();
};

class Interpreter {
public:
    std::vector<std::map<std::string, ValuePtr>> scopes;
    std::vector<std::string> output;

public:
    Interpreter();
    ~Interpreter();

    void pushScope();
    void popScope();
    ValuePtr getVar(const std::string& name);
    void setVar(const std::string& name, ValuePtr value);
    std::string run(std::shared_ptr<ProgramNode> ast);
    const std::vector<std::string>& getOutput() const;
    void clearOutput();
    void initBuiltins();

    static ValuePtr getIndex(ValuePtr obj, ValuePtr idx);
    static void setIndex(ValuePtr obj, ValuePtr idx, ValuePtr val);
    static ValuePtr getMember(ValuePtr obj, const std::string& member);
    static void setMember(ValuePtr obj, const std::string& member, ValuePtr val);

    void* allocMemory(size_t size);
    void freeMemory(void* ptr);

    template<typename T>
    T readMemory(void* ptr) {
        return *static_cast<T*>(ptr);
    }

    template<typename T>
    void writeMemory(void* ptr, T value) {
        *static_cast<T*>(ptr) = value;
    }

    uint64_t syscall(uint64_t num, uint64_t a1, uint64_t a2, uint64_t a3,
        uint64_t a4, uint64_t a5, uint64_t a6);
};

void InitStdLib(Interpreter& interpreter);
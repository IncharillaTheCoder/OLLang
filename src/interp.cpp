#include "ollang.h"
#include <stdexcept>
#include <algorithm>
#include <iostream>
#include <cmath>
#include <cstring>
#include <windows.h>
#include <fstream>
#include <filesystem>
#include <sstream>
#include <unordered_map>
#include <random>
#include <ctime>
#include <future>
#include <curl/curl.h> // make sure to install libcurl more info in ollang.cpp

// Global caches
static std::unordered_map<std::string, std::shared_ptr<ProgramNode>> importCache;
static std::unordered_map<HMODULE, int> dllRefCount;

// Constructor implementations
NumberNode::NumberNode(double v) : value(v) {}
StringNode::StringNode(const std::string& v) : value(v) {}
BooleanNode::BooleanNode(bool v) : value(v) {}
IdentifierNode::IdentifierNode(const std::string& n) : name(n) {}

// Node evaluation implementations
ValuePtr ProgramNode::eval(Interpreter& i) {
    for (auto& s : body) s->eval(i);
    return std::make_shared<NullValue>();
}

ValuePtr ExpressionStatementNode::eval(Interpreter& i) {
    return expression->eval(i);
}

ValuePtr NumberNode::eval(Interpreter& i) {
    return std::make_shared<NumberValue>(value);
}

ValuePtr StringNode::eval(Interpreter& i) {
    return std::make_shared<StringValue>(value);
}

ValuePtr BooleanNode::eval(Interpreter& i) {
    return std::make_shared<BooleanValue>(value);
}

ValuePtr IdentifierNode::eval(Interpreter& i) {
    return i.getVar(name);
}

ValuePtr AssignmentNode::eval(Interpreter& i) {
    auto v = value->eval(i);
    i.setVar(name, v);
    return v;
}

ValuePtr BinaryOpNode::eval(Interpreter& i) {
    auto l = left->eval(i);
    auto r = right->eval(i);

    if (op == "**") {
        auto ln = dynamic_cast<NumberValue*>(l.get());
        auto rn = dynamic_cast<NumberValue*>(r.get());
        if (ln && rn) {
            return std::make_shared<NumberValue>(pow(ln->value, rn->value));
        }
        throw std::runtime_error("Power operator (**) requires number operands. Got " +
            l->toString() + " and " + r->toString());
    }

    auto ln = dynamic_cast<NumberValue*>(l.get());
    auto rn = dynamic_cast<NumberValue*>(r.get());

    if (ln && rn) {
        double a = ln->value, b = rn->value;
        if (op == "+") return std::make_shared<NumberValue>(a + b);
        if (op == "-") return std::make_shared<NumberValue>(a - b);
        if (op == "*") return std::make_shared<NumberValue>(a * b);
        if (op == "/") {
            if (b == 0) return std::make_shared<NumberValue>(0);
            return std::make_shared<NumberValue>(a / b);
        }
        if (op == "%") return std::make_shared<NumberValue>(fmod(a, b));
        if (op == "<") return std::make_shared<BooleanValue>(a < b);
        if (op == ">") return std::make_shared<BooleanValue>(a > b);
        if (op == "<=") return std::make_shared<BooleanValue>(a <= b);
        if (op == ">=") return std::make_shared<BooleanValue>(a >= b);
        if (op == "==") return std::make_shared<BooleanValue>(a == b);
        if (op == "!=") return std::make_shared<BooleanValue>(a != b);

        int64_t ia = static_cast<int64_t>(a);
        int64_t ib = static_cast<int64_t>(b);
        if (op == "&") return std::make_shared<NumberValue>(static_cast<double>(ia & ib));
        if (op == "|") return std::make_shared<NumberValue>(static_cast<double>(ia | ib));
        if (op == "^") return std::make_shared<NumberValue>(static_cast<double>(ia ^ ib));
        if (op == "<<") return std::make_shared<NumberValue>(static_cast<double>(ia << ib));
        if (op == ">>") return std::make_shared<NumberValue>(static_cast<double>(ia >> ib));
    }

    if (op == "+") {
        return std::make_shared<StringValue>(l->toString() + r->toString());
    }

    if (op == "==") {
        if (ln && rn) return std::make_shared<BooleanValue>(ln->value == rn->value);
        auto ls = dynamic_cast<StringValue*>(l.get());
        auto rs = dynamic_cast<StringValue*>(r.get());
        if (ls && rs) {
            return std::make_shared<BooleanValue>(ls->value == rs->value);
        }
        return std::make_shared<BooleanValue>(l->toString() == r->toString());
    }
    if (op == "!=") {
        if (ln && rn) return std::make_shared<BooleanValue>(ln->value != rn->value);
        auto ls = dynamic_cast<StringValue*>(l.get());
        auto rs = dynamic_cast<StringValue*>(r.get());
        if (ls && rs) {
            return std::make_shared<BooleanValue>(ls->value != rs->value);
        }
        return std::make_shared<BooleanValue>(l->toString() != r->toString());
    }

    throw std::runtime_error("Invalid operation: '" + op + "' between " + l->toString() + " and " + r->toString());
}

ValuePtr UnaryOpNode::eval(Interpreter& i) {
    auto v = operand->eval(i);
    if (op == "-") {
        if (auto n = dynamic_cast<NumberValue*>(v.get()))
            return std::make_shared<NumberValue>(-n->value);
    }
    else if (op == "!") {
        return std::make_shared<BooleanValue>(!v->isTruthy());
    }
    else if (op == "~") {
        if (auto n = dynamic_cast<NumberValue*>(v.get())) {
            int64_t val = static_cast<int64_t>(n->value);
            return std::make_shared<NumberValue>(static_cast<double>(~val));
        }
    }
    throw std::runtime_error("Invalid unary operator: " + op);
}

ValuePtr FunctionDefNode::eval(Interpreter& i) {
    auto f = std::make_shared<FunctionValue>(name, params, body);
    for (auto& [k, v] : i.scopes.back()) f->closure[k] = v;
    i.setVar(name, f);
    return std::make_shared<NullValue>();
}

ValuePtr CallNode::eval(Interpreter& i) {
    auto calleeValue = callee->eval(i);
    std::vector<ValuePtr> args;
    for (auto& a : arguments) args.push_back(a->eval(i));

    if (auto b = dynamic_cast<BuiltinValue*>(calleeValue.get())) {
        return b->func(args);
    }

    if (auto f = dynamic_cast<FunctionValue*>(calleeValue.get())) {
        i.pushScope();
        for (auto& [k, v] : f->closure) i.scopes.back()[k] = v;
        for (size_t j = 0; j < f->params.size(); j++)
            i.scopes.back()[f->params[j]] = args[j];

        ValuePtr result = std::make_shared<NullValue>();
        for (auto& s : f->body) {
            result = s->eval(i);
            if (dynamic_cast<ReturnNode*>(s.get())) break;
        }
        i.popScope();
        return result;
    }

    throw std::runtime_error("Not a function");
}

ValuePtr IfNode::eval(Interpreter& i) {
    auto cond = condition->eval(i);
    i.pushScope();
    try {
        if (cond->isTruthy()) {
            for (auto& s : thenBranch) {
                s->eval(i);
                if (dynamic_cast<ReturnNode*>(s.get())) break;
            }
        }
        else if (!elseBranch.empty()) {
            for (auto& s : elseBranch) {
                s->eval(i);
                if (dynamic_cast<ReturnNode*>(s.get())) break;
            }
        }
    }
    catch (...) {
        i.popScope();
        throw;
    }
    i.popScope();
    return std::make_shared<NullValue>();
}

ValuePtr WhileNode::eval(Interpreter& i) {
    while (condition->eval(i)->isTruthy()) {
        i.pushScope();
        try {
            for (auto& s : body) {
                s->eval(i);
                if (dynamic_cast<ReturnNode*>(s.get())) break;
            }
        }
        catch (...) {
            i.popScope();
            throw;
        }
        i.popScope();
    }
    return std::make_shared<NullValue>();
}

ValuePtr ForNode::eval(Interpreter& i) {
    auto iter = iterable->eval(i);
    if (auto arr = dynamic_cast<ArrayValue*>(iter.get())) {
        for (auto& elem : arr->elements) {
            i.pushScope();
            i.setVar(var, elem);
            try {
                for (auto& s : body) {
                    s->eval(i);
                    if (dynamic_cast<ReturnNode*>(s.get())) break;
                }
            }
            catch (...) {
                i.popScope();
                throw;
            }
            i.popScope();
        }
    }
    else if (auto str = dynamic_cast<StringValue*>(iter.get())) {
        for (char c : str->value) {
            i.pushScope();
            i.setVar(var, std::make_shared<StringValue>(std::string(1, c)));
            try {
                for (auto& s : body) {
                    s->eval(i);
                    if (dynamic_cast<ReturnNode*>(s.get())) break;
                }
            }
            catch (...) {
                i.popScope();
                throw;
            }
            i.popScope();
        }
    }
    return std::make_shared<NullValue>();
}

ValuePtr NamespaceNode::eval(Interpreter& i) {
    i.pushScope();
    for (auto& stmt : body) stmt->eval(i);
    auto namespaceScope = i.scopes.back();
    i.popScope();

    auto dict = std::make_shared<DictValue>();
    for (auto& [key, value] : namespaceScope) {
        dict->items[key] = value;
    }
    i.setVar(name, dict);
    return dict;
}

ValuePtr ListComprehensionNode::eval(Interpreter& i) {
    auto arr = std::make_shared<ArrayValue>();
    auto iterVal = iterable->eval(i);

    if (auto list = dynamic_cast<ArrayValue*>(iterVal.get())) {
        for (auto& elem : list->elements) {
            i.pushScope();
            i.setVar(var, elem);

            bool include = true;
            if (condition) {
                auto condResult = condition->eval(i);
                include = condResult->isTruthy();
            }

            if (include) {
                auto result = expression->eval(i);
                arr->elements.push_back(result);
            }

            i.popScope();
        }
    }
    else if (auto str = dynamic_cast<StringValue*>(iterVal.get())) {
        for (char c : str->value) {
            i.pushScope();
            i.setVar(var, std::make_shared<StringValue>(std::string(1, c)));

            bool include = true;
            if (condition) {
                auto condResult = condition->eval(i);
                include = condResult->isTruthy();
            }

            if (include) {
                auto result = expression->eval(i);
                arr->elements.push_back(result);
            }

            i.popScope();
        }
    }
    else {
        throw std::runtime_error("List comprehension requires array or string");
    }

    return arr;
}

ValuePtr ThrowNode::eval(Interpreter& i) {
    auto val = value->eval(i);
    throw std::runtime_error(val->toString());
    return std::make_shared<NullValue>();
}

ValuePtr ReturnNode::eval(Interpreter& i) {
    return value ? value->eval(i) : std::make_shared<NullValue>();
}

ValuePtr ArrayNode::eval(Interpreter& i) {
    auto arr = std::make_shared<ArrayValue>();
    for (auto& e : elements) arr->elements.push_back(e->eval(i));
    return arr;
}

ValuePtr DictNode::eval(Interpreter& i) {
    auto dict = std::make_shared<DictValue>();
    for (auto& [k, v] : entries) dict->items[k] = v->eval(i);
    return dict;
}

ValuePtr IndexNode::eval(Interpreter& i) {
    auto obj = object->eval(i);
    auto idx = index->eval(i);
    return Interpreter::getIndex(obj, idx);
}

ValuePtr DotNode::eval(Interpreter& i) {
    auto obj = object->eval(i);
    return Interpreter::getMember(obj, member);
}

ValuePtr IndexAssignNode::eval(Interpreter& i) {
    auto obj = object->eval(i);
    auto idx = index->eval(i);
    auto val = value->eval(i);
    Interpreter::setIndex(obj, idx, val);
    return val;
}

ValuePtr DotAssignNode::eval(Interpreter& i) {
    auto obj = object->eval(i);
    auto val = value->eval(i);
    Interpreter::setMember(obj, member, val);
    return val;
}

ValuePtr NullNode::eval(Interpreter& interpreter) {
    return std::make_shared<NullValue>();
}

ValuePtr ImportNode::eval(Interpreter& i) {
    std::string modulePath = module;

    if (modulePath.find('.') == std::string::npos) {
        modulePath += ".oll";
    }

    if (importCache.find(modulePath) != importCache.end()) {
        importCache[modulePath]->eval(i);
        return std::make_shared<NullValue>();
    }

    std::ifstream file(modulePath);
    if (!file.is_open()) {
        throw std::runtime_error("Cannot import module: " + modulePath);
    }

    std::stringstream buffer;
    buffer << file.rdbuf();
    std::string source = buffer.str();
    file.close();

    Lexer lexer(source);
    auto tokens = lexer.tokenize();
    Parser parser(tokens);
    auto ast = parser.parse();

    importCache[modulePath] = ast;
    ast->eval(i);

    return std::make_shared<NullValue>();
}

// DLL function value
struct DLLFunctionValue : BuiltinValue {
    FARPROC funcPtr;
    HMODULE hModule;

    DLLFunctionValue(const std::string& n, FARPROC ptr, HMODULE hMod)
        : BuiltinValue(n, nullptr), funcPtr(ptr), hModule(hMod) {
        dllRefCount[hModule]++;
    }

    ~DLLFunctionValue() {
        dllRefCount[hModule]--;
        if (dllRefCount[hModule] <= 0) {
            FreeLibrary(hModule);
            dllRefCount.erase(hModule);
        }
    }

    std::string toString() const override {
        return "<dll function " + name + ">";
    }
};

ValuePtr ImportDLLNode::eval(Interpreter& i) {
    HMODULE hModule = LoadLibraryA(dllPath.c_str());
    if (!hModule) {
        throw std::runtime_error("Failed to load DLL: " + dllPath);
    }

    FARPROC funcPtr = GetProcAddress(hModule, functionName.c_str());
    if (!funcPtr) {
        FreeLibrary(hModule);
        throw std::runtime_error("Function not found in DLL: " + functionName);
    }

    auto dllFunc = std::make_shared<DLLFunctionValue>(alias, funcPtr, hModule);

    dllFunc->func = [funcPtr, functionName = this->functionName](const std::vector<ValuePtr>& args) -> ValuePtr {
        int argCount = static_cast<int>(args.size());

        if (functionName == "MessageBoxA" && argCount >= 4) {
            auto text = dynamic_cast<StringValue*>(args[0].get());
            auto caption = dynamic_cast<StringValue*>(args[1].get());
            auto type = dynamic_cast<NumberValue*>(args[2].get());
            auto hwnd = dynamic_cast<NumberValue*>(args[3].get());

            if (text && caption && type && hwnd) {
                int result = MessageBoxA(
                    reinterpret_cast<HWND>(static_cast<intptr_t>(hwnd->value)),
                    text->value.c_str(),
                    caption->value.c_str(),
                    static_cast<UINT>(type->value)
                );
                return std::make_shared<NumberValue>(result);
            }
        }

        if (functionName == "GetCurrentProcessId") {
            DWORD pid = GetCurrentProcessId();
            return std::make_shared<NumberValue>(pid);
        }

        if (functionName == "GetCurrentThreadId") {
            DWORD tid = GetCurrentThreadId();
            return std::make_shared<NumberValue>(tid);
        }

        if (functionName == "GetTickCount") {
            DWORD ticks = GetTickCount();
            return std::make_shared<NumberValue>(ticks);
        }

        return std::make_shared<NullValue>();
        };

    i.setVar(alias, dllFunc);
    return dllFunc;
}

ValuePtr SyscallNode::eval(Interpreter& i) {
    auto numVal = syscallNum->eval(i);
    std::vector<uint64_t> args;
    for (auto& arg : arguments) {
        auto val = arg->eval(i);
        if (auto num = dynamic_cast<NumberValue*>(val.get())) {
            args.push_back(static_cast<uint64_t>(num->value));
        }
        else {
            throw std::runtime_error("Syscall arguments must be numbers");
        }
    }

    while (args.size() < 6) args.push_back(0);

    uint64_t result = i.syscall(static_cast<uint64_t>(dynamic_cast<NumberValue*>(numVal.get())->value),
        args[0], args[1], args[2], args[3], args[4], args[5]);
    return std::make_shared<NumberValue>(static_cast<double>(result));
}

ValuePtr AllocNode::eval(Interpreter& i) {
    auto sizeVal = size->eval(i);
    if (auto num = dynamic_cast<NumberValue*>(sizeVal.get())) {
        size_t allocSize = static_cast<size_t>(num->value);
        void* ptr = i.allocMemory(allocSize);
        return std::make_shared<PointerValue>(ptr, allocSize, true);
    }
    throw std::runtime_error("alloc requires number");
}

ValuePtr FreeNode::eval(Interpreter& i) {
    auto ptrVal = ptr->eval(i);
    if (auto p = dynamic_cast<PointerValue*>(ptrVal.get())) {
        i.freeMemory(p->ptr);
        return std::make_shared<NullValue>();
    }
    throw std::runtime_error("free requires pointer");
}

ValuePtr ReadMemNode::eval(Interpreter& i) {
    auto ptrVal = ptr->eval(i);
    auto offsetVal = offset->eval(i);

    if (auto p = dynamic_cast<PointerValue*>(ptrVal.get())) {
        if (auto off = dynamic_cast<NumberValue*>(offsetVal.get())) {
            size_t offset = static_cast<size_t>(off->value);
            void* addr = static_cast<char*>(p->ptr) + offset;

            if (type == "i8") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<int8_t>(addr)));
            if (type == "u8") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<uint8_t>(addr)));
            if (type == "i16") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<int16_t>(addr)));
            if (type == "u16") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<uint16_t>(addr)));
            if (type == "i32") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<int32_t>(addr)));
            if (type == "u32") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<uint32_t>(addr)));
            if (type == "i64") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<int64_t>(addr)));
            if (type == "u64") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<uint64_t>(addr)));
            if (type == "f32") return std::make_shared<NumberValue>(static_cast<double>(i.readMemory<float>(addr)));
            if (type == "f64") return std::make_shared<NumberValue>(i.readMemory<double>(addr));
        }
    }
    throw std::runtime_error("Invalid memory read");
}

ValuePtr WriteMemNode::eval(Interpreter& i) {
    auto ptrVal = ptr->eval(i);
    auto offsetVal = offset->eval(i);
    auto valueVal = value->eval(i);

    if (auto p = dynamic_cast<PointerValue*>(ptrVal.get())) {
        if (auto off = dynamic_cast<NumberValue*>(offsetVal.get())) {
            if (auto val = dynamic_cast<NumberValue*>(valueVal.get())) {
                size_t offset = static_cast<size_t>(off->value);
                void* addr = static_cast<char*>(p->ptr) + offset;

                if (type == "i8") i.writeMemory<int8_t>(addr, static_cast<int8_t>(val->value));
                else if (type == "u8") i.writeMemory<uint8_t>(addr, static_cast<uint8_t>(val->value));
                else if (type == "i16") i.writeMemory<int16_t>(addr, static_cast<int16_t>(val->value));
                else if (type == "u16") i.writeMemory<uint16_t>(addr, static_cast<uint16_t>(val->value));
                else if (type == "i32") i.writeMemory<int32_t>(addr, static_cast<int32_t>(val->value));
                else if (type == "u32") i.writeMemory<uint32_t>(addr, static_cast<uint32_t>(val->value));
                else if (type == "i64") i.writeMemory<int64_t>(addr, static_cast<int64_t>(val->value));
                else if (type == "u64") i.writeMemory<uint64_t>(addr, static_cast<uint64_t>(val->value));
                else if (type == "f32") i.writeMemory<float>(addr, static_cast<float>(val->value));
                else if (type == "f64") i.writeMemory<double>(addr, val->value);
                else throw std::runtime_error("Invalid type for memory write");

                return valueVal;
            }
        }
    }
    throw std::runtime_error("Invalid memory write");
}

ValuePtr TryCatchNode::eval(Interpreter& i) {
    try {
        i.pushScope();
        for (auto& s : tryBody) {
            s->eval(i);
        }
        i.popScope();
    }
    catch (const std::exception& e) {
        i.pushScope();
        i.setVar(catchVar, std::make_shared<StringValue>(e.what()));
        for (auto& s : catchBody) {
            s->eval(i);
        }
        i.popScope();
    }
    return std::make_shared<NullValue>();
}

// Promise value for async operations
struct PromiseValue : Value {
    std::future<ValuePtr> future;
    bool resolved = false;
    ValuePtr result;

    PromiseValue(std::future<ValuePtr>&& f) : future(std::move(f)) {}

    std::string toString() const override {
        return resolved ? result->toString() : "<pending promise>";
    }

    bool isTruthy() const override {
        return true;
    }

    ValuePtr await() {
        if (!resolved) {
            result = future.get();
            resolved = true;
        }
        return result;
    }
};

// Async function value
struct AsyncFunctionValue : FunctionValue {
    AsyncFunctionValue(const std::string& n, const std::vector<std::string>& p,
        const std::vector<std::shared_ptr<Node>>& b)
        : FunctionValue(n, p, b) {
    }

    std::string toString() const override {
        return "<async function " + name + ">";
    }

    ValuePtr callAsync(Interpreter& i, const std::vector<ValuePtr>& args) {
        auto promise = std::make_shared<PromiseValue>(
            std::async(std::launch::async, [this, &i, args]() -> ValuePtr {
                i.pushScope();
                for (auto& [k, v] : this->closure) i.scopes.back()[k] = v;
                for (size_t j = 0; j < this->params.size(); j++)
                    i.scopes.back()[this->params[j]] = args[j];

                ValuePtr result = std::make_shared<NullValue>();
                for (auto& s : this->body) {
                    result = s->eval(i);
                    if (dynamic_cast<ReturnNode*>(s.get())) break;
                }
                i.popScope();
                return result;
                })
        );
        return promise;
    }
};

ValuePtr AsyncFunctionDefNode::eval(Interpreter& i) {
    auto f = std::make_shared<AsyncFunctionValue>(name, params, body);
    for (auto& [k, v] : i.scopes.back()) f->closure[k] = v;
    i.setVar(name, f);
    return std::make_shared<NullValue>();
}

ValuePtr AwaitNode::eval(Interpreter& i) {
    auto promiseValue = expression->eval(i);
    if (auto promise = dynamic_cast<PromiseValue*>(promiseValue.get())) {
        return promise->await();
    }
    if (auto asyncFunc = dynamic_cast<AsyncFunctionValue*>(promiseValue.get())) {
        auto result = std::make_shared<PromiseValue>(
            std::async(std::launch::async, [asyncFunc, &i]() -> ValuePtr {
                return asyncFunc->callAsync(i, {});
                })
        );
        return result->await();
    }
    throw std::runtime_error("Cannot await non-promise value");
}

// Interpreter constructor and basic methods
Interpreter::Interpreter() {
    scopes.push_back({});
    initBuiltins();
}

void Interpreter::clearOutput() {
    output.clear();
}

Interpreter::~Interpreter() {}

void Interpreter::pushScope() { scopes.push_back({}); }
void Interpreter::popScope() { scopes.pop_back(); }

ValuePtr Interpreter::getVar(const std::string& name) {
    for (int i = static_cast<int>(scopes.size()) - 1; i >= 0; i--) {
        if (scopes[i].count(name)) return scopes[i][name];
    }
    throw std::runtime_error("Undefined variable: " + name);
}

void Interpreter::setVar(const std::string& name, ValuePtr v) {
    scopes.back()[name] = v;
}

std::string Interpreter::run(std::shared_ptr<ProgramNode> ast) {
    output.clear();
    try {
        ast->eval(*this);
    }
    catch (const std::exception& e) {
        return "Error: " + std::string(e.what());
    }

    std::string result;
    for (size_t i = 0; i < output.size(); i++) {
        if (i > 0) result += "\n";
        result += output[i];
    }
    return result;
}

const std::vector<std::string>& Interpreter::getOutput() const {
    return output;
}

ValuePtr Interpreter::getIndex(ValuePtr obj, ValuePtr idx) {
    if (auto arr = dynamic_cast<ArrayValue*>(obj.get())) {
        if (auto num = dynamic_cast<NumberValue*>(idx.get())) {
            int index = static_cast<int>(num->value);
            if (index >= 0 && index < arr->elements.size()) {
                return arr->elements[index];
            }
            throw std::runtime_error("Array index out of bounds");
        }
    }
    else if (auto str = dynamic_cast<StringValue*>(obj.get())) {
        if (auto num = dynamic_cast<NumberValue*>(idx.get())) {
            int index = static_cast<int>(num->value);
            if (index >= 0 && index < str->value.size()) {
                return std::make_shared<StringValue>(std::string(1, str->value[index]));
            }
            throw std::runtime_error("String index out of bounds");
        }
    }
    else if (auto dict = dynamic_cast<DictValue*>(obj.get())) {
        if (auto key = dynamic_cast<StringValue*>(idx.get())) {
            auto it = dict->items.find(key->value);
            if (it != dict->items.end()) return it->second;
            throw std::runtime_error("Key not found: " + key->value);
        }
    }
    throw std::runtime_error("Cannot index this type");
}

void Interpreter::setIndex(ValuePtr obj, ValuePtr idx, ValuePtr val) {
    if (auto arr = dynamic_cast<ArrayValue*>(obj.get())) {
        if (auto num = dynamic_cast<NumberValue*>(idx.get())) {
            int index = static_cast<int>(num->value);
            if (index >= 0) {
                if (index >= arr->elements.size()) {
                    arr->elements.resize(index + 1);
                }
                arr->elements[index] = val;
                return;
            }
            throw std::runtime_error("Array index out of bounds");
        }
    }
    else if (auto dict = dynamic_cast<DictValue*>(obj.get())) {
        if (auto key = dynamic_cast<StringValue*>(idx.get())) {
            dict->items[key->value] = val;
            return;
        }
    }
    throw std::runtime_error("Cannot index this type");
}

ValuePtr Interpreter::getMember(ValuePtr obj, const std::string& member) {
    if (auto dict = dynamic_cast<DictValue*>(obj.get())) {
        auto it = dict->items.find(member);
        if (it != dict->items.end()) return it->second;
        throw std::runtime_error("Key not found: " + member);
    }
    throw std::runtime_error("Cannot access member of this type");
}

void Interpreter::setMember(ValuePtr obj, const std::string& member, ValuePtr val) {
    if (auto dict = dynamic_cast<DictValue*>(obj.get())) {
        dict->items[member] = val;
        return;
    }
    throw std::runtime_error("Cannot set member of this type");
}

void* Interpreter::allocMemory(size_t size) {
    return malloc(size);
}

void Interpreter::freeMemory(void* ptr) {
    free(ptr);
}

uint64_t Interpreter::syscall(uint64_t num, uint64_t a1, uint64_t a2, uint64_t a3,
    uint64_t a4, uint64_t a5, uint64_t a6) {
    switch (num) {
    case 0x001: {
        const char* filename = reinterpret_cast<const char*>(a1);
        const char* content = reinterpret_cast<const char*>(a2);
        size_t len = static_cast<size_t>(a3);

        try {
            std::ofstream file(filename);
            if (!file.is_open()) return 0;

            if (content && len > 0) {
                file.write(content, len);
            }
            file.close();
            return 1;
        }
        catch (...) {
            return 0;
        }
    }
    case 0x002: {
        const char* filename = reinterpret_cast<const char*>(a1);
        void* buffer = reinterpret_cast<void*>(a2);
        size_t size = static_cast<size_t>(a3);

        try {
            std::ifstream file(filename, std::ios::binary);
            if (!file.is_open()) return 0;

            file.read(static_cast<char*>(buffer), size);
            return file.gcount();
        }
        catch (...) {
            return 0;
        }
    }
    case 0x003: {
        const char* filename = reinterpret_cast<const char*>(a1);
        try {
            return std::filesystem::remove(filename) ? 1 : 0;
        }
        catch (...) {
            return 0;
        }
    }
    default:
        return 0;
    }
}

// Initialize all built-in/stdlib functions
void Interpreter::initBuiltins() {
    // Basic I/O 
    scopes[0]["print"] = std::make_shared<BuiltinValue>("print",
        [this](const std::vector<ValuePtr>& args) -> ValuePtr {
            std::string s;
            for (size_t i = 0; i < args.size(); i++) {
                if (i > 0) s += " ";
                s += args[i]->toString();
            }
            output.push_back(s);
            return std::make_shared<NullValue>();
        });

    scopes[0]["println"] = std::make_shared<BuiltinValue>("println",
        [this](const std::vector<ValuePtr>& args) -> ValuePtr {
            std::string s;
            for (size_t i = 0; i < args.size(); i++) {
                if (i > 0) s += " ";
                s += args[i]->toString();
            }
            output.push_back(s);
            return std::make_shared<NullValue>();
        });

    // Math functions
    scopes[0]["abs"] = std::make_shared<BuiltinValue>("abs",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("abs expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("abs requires number");
            return std::make_shared<NumberValue>(fabs(num->value));
        });

    scopes[0]["sqrt"] = std::make_shared<BuiltinValue>("sqrt",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("sqrt expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("sqrt requires number");
            return std::make_shared<NumberValue>(sqrt(num->value));
        });

    scopes[0]["pow"] = std::make_shared<BuiltinValue>("pow",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("pow expects 2 arguments");
            auto base = dynamic_cast<NumberValue*>(args[0].get());
            auto exp = dynamic_cast<NumberValue*>(args[1].get());
            if (!base || !exp) throw std::runtime_error("pow requires numbers");
            return std::make_shared<NumberValue>(pow(base->value, exp->value));
        });

    scopes[0]["sin"] = std::make_shared<BuiltinValue>("sin",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("sin expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("sin requires number");
            return std::make_shared<NumberValue>(sin(num->value));
        });

    scopes[0]["cos"] = std::make_shared<BuiltinValue>("cos",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("cos expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("cos requires number");
            return std::make_shared<NumberValue>(cos(num->value));
        });

    scopes[0]["tan"] = std::make_shared<BuiltinValue>("tan",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("tan expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("tan requires number");
            return std::make_shared<NumberValue>(tan(num->value));
        });

    scopes[0]["log"] = std::make_shared<BuiltinValue>("log",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("log expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("log requires number");
            return std::make_shared<NumberValue>(log(num->value));
        });

    scopes[0]["exp"] = std::make_shared<BuiltinValue>("exp",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("exp expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("exp requires number");
            return std::make_shared<NumberValue>(exp(num->value));
        });

    // Random number generation
    static std::mt19937 rng(static_cast<unsigned int>(time(nullptr)));

    scopes[0]["rand"] = std::make_shared<BuiltinValue>("rand",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() > 2) throw std::runtime_error("rand expects 0-2 arguments");
            if (args.empty()) {
                std::uniform_real_distribution<double> dist(0.0, 1.0);
                return std::make_shared<NumberValue>(dist(rng));
            }
            else if (args.size() == 1) {
                auto max = dynamic_cast<NumberValue*>(args[0].get());
                if (!max) throw std::runtime_error("rand requires number");
                std::uniform_real_distribution<double> dist(0.0, max->value);
                return std::make_shared<NumberValue>(dist(rng));
            }
            else {
                auto min = dynamic_cast<NumberValue*>(args[0].get());
                auto max = dynamic_cast<NumberValue*>(args[1].get());
                if (!min || !max) throw std::runtime_error("rand requires numbers");
                std::uniform_real_distribution<double> dist(min->value, max->value);
                return std::make_shared<NumberValue>(dist(rng));
            }
        });

    scopes[0]["randint"] = std::make_shared<BuiltinValue>("randint",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("randint expects 2 arguments");
            auto min = dynamic_cast<NumberValue*>(args[0].get());
            auto max = dynamic_cast<NumberValue*>(args[1].get());
            if (!min || !max) throw std::runtime_error("randint requires numbers");
            std::uniform_int_distribution<int> dist(static_cast<int>(min->value), static_cast<int>(max->value));
            return std::make_shared<NumberValue>(dist(rng));
        });

    // String manipulation functions
    scopes[0]["upper"] = std::make_shared<BuiltinValue>("upper",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("upper expects 1 argument");
            auto str = dynamic_cast<StringValue*>(args[0].get());
            if (!str) throw std::runtime_error("upper requires string");
            std::string result = str->value;
            std::transform(result.begin(), result.end(), result.begin(), ::toupper);
            return std::make_shared<StringValue>(result);
        });

    scopes[0]["lower"] = std::make_shared<BuiltinValue>("lower",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("lower expects 1 argument");
            auto str = dynamic_cast<StringValue*>(args[0].get());
            if (!str) throw std::runtime_error("lower requires string");
            std::string result = str->value;
            std::transform(result.begin(), result.end(), result.begin(), ::tolower);
            return std::make_shared<StringValue>(result);
        });

    scopes[0]["trim"] = std::make_shared<BuiltinValue>("trim",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("trim expects 1 argument");
            auto str = dynamic_cast<StringValue*>(args[0].get());
            if (!str) throw std::runtime_error("trim requires string");
            std::string result = str->value;
            result.erase(result.begin(), std::find_if(result.begin(), result.end(),
                [](unsigned char ch) { return !std::isspace(ch); }));
            result.erase(std::find_if(result.rbegin(), result.rend(),
                [](unsigned char ch) { return !std::isspace(ch); }).base(), result.end());
            return std::make_shared<StringValue>(result);
        });

    scopes[0]["split"] = std::make_shared<BuiltinValue>("split",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("split expects 2 arguments");
            auto str = dynamic_cast<StringValue*>(args[0].get());
            auto delim = dynamic_cast<StringValue*>(args[1].get());
            if (!str || !delim) throw std::runtime_error("split requires strings");

            auto arr = std::make_shared<ArrayValue>();
            size_t start = 0, end;
            while ((end = str->value.find(delim->value, start)) != std::string::npos) {
                arr->elements.push_back(std::make_shared<StringValue>(str->value.substr(start, end - start)));
                start = end + delim->value.length();
            }
            arr->elements.push_back(std::make_shared<StringValue>(str->value.substr(start)));
            return arr;
        });

    // Array functions
    scopes[0]["len"] = std::make_shared<BuiltinValue>("len",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("len expects 1 argument");
            if (auto arr = dynamic_cast<ArrayValue*>(args[0].get())) {
                return std::make_shared<NumberValue>(arr->elements.size());
            }
            if (auto str = dynamic_cast<StringValue*>(args[0].get())) {
                return std::make_shared<NumberValue>(str->value.size());
            }
            throw std::runtime_error("len requires array or string");
        });

    scopes[0]["push"] = std::make_shared<BuiltinValue>("push",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("push expects 2 arguments");
            auto arr = dynamic_cast<ArrayValue*>(args[0].get());
            if (!arr) throw std::runtime_error("push requires array");
            arr->elements.push_back(args[1]);
            return std::make_shared<NumberValue>(arr->elements.size());
        });

    scopes[0]["pop"] = std::make_shared<BuiltinValue>("pop",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("pop expects 1 argument");
            auto arr = dynamic_cast<ArrayValue*>(args[0].get());
            if (!arr) throw std::runtime_error("pop requires array");
            if (arr->elements.empty()) return std::make_shared<NullValue>();
            auto last = arr->elements.back();
            arr->elements.pop_back();
            return last;
        });

    scopes[0]["range"] = std::make_shared<BuiltinValue>("range",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() == 1) {
                auto end = dynamic_cast<NumberValue*>(args[0].get());
                if (!end) throw std::runtime_error("range requires number");
                auto arr = std::make_shared<ArrayValue>();
                for (int i = 0; i < static_cast<int>(end->value); i++) {
                    arr->elements.push_back(std::make_shared<NumberValue>(i));
                }
                return arr;
            }
            else if (args.size() == 2) {
                auto start = dynamic_cast<NumberValue*>(args[0].get());
                auto end = dynamic_cast<NumberValue*>(args[1].get());
                if (!start || !end) throw std::runtime_error("range requires numbers");
                auto arr = std::make_shared<ArrayValue>();
                for (int i = static_cast<int>(start->value); i < static_cast<int>(end->value); i++) {
                    arr->elements.push_back(std::make_shared<NumberValue>(i));
                }
                return arr;
            }
            else if (args.size() == 3) {
                auto start = dynamic_cast<NumberValue*>(args[0].get());
                auto end = dynamic_cast<NumberValue*>(args[1].get());
                auto step = dynamic_cast<NumberValue*>(args[2].get());
                if (!start || !end || !step) throw std::runtime_error("range requires numbers");
                auto arr = std::make_shared<ArrayValue>();
                for (double i = start->value; i < end->value; i += step->value) {
                    arr->elements.push_back(std::make_shared<NumberValue>(i));
                }
                return arr;
            }
            throw std::runtime_error("range expects 1-3 arguments");
        });

    // File operations
    scopes[0]["file_write"] = std::make_shared<BuiltinValue>("file_write",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() < 2) throw std::runtime_error("file_write expects filename and content");
            auto filename = dynamic_cast<StringValue*>(args[0].get());
            if (!filename) throw std::runtime_error("file_write requires string filename");

            std::ofstream file(filename->value);
            if (!file.is_open()) return std::make_shared<NumberValue>(0);

            std::string content = args[1]->toString();
            file << content;
            file.close();
            return std::make_shared<NumberValue>(1);
        });

    scopes[0]["file_read"] = std::make_shared<BuiltinValue>("file_read",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("file_read expects filename");
            auto filename = dynamic_cast<StringValue*>(args[0].get());
            if (!filename) throw std::runtime_error("file_read requires string filename");

            std::ifstream file(filename->value);
            if (!file.is_open()) return std::make_shared<StringValue>("");

            std::string content((std::istreambuf_iterator<char>(file)),
                std::istreambuf_iterator<char>());
            return std::make_shared<StringValue>(content);
        });

    scopes[0]["file_append"] = std::make_shared<BuiltinValue>("file_append",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() < 2) throw std::runtime_error("file_append expects filename and content");
            auto filename = dynamic_cast<StringValue*>(args[0].get());
            if (!filename) throw std::runtime_error("file_append requires string filename");

            std::ofstream file(filename->value, std::ios::app);
            if (!file.is_open()) return std::make_shared<NumberValue>(0);

            std::string content = args[1]->toString();
            file << content;
            file.close();
            return std::make_shared<NumberValue>(1);
        });

    scopes[0]["file_exists"] = std::make_shared<BuiltinValue>("file_exists",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("file_exists expects filename");
            auto filename = dynamic_cast<StringValue*>(args[0].get());
            if (!filename) throw std::runtime_error("file_exists requires string filename");

            try {
                bool exists = std::filesystem::exists(filename->value);
                return std::make_shared<BooleanValue>(exists);
            }
            catch (...) {
                return std::make_shared<BooleanValue>(false);
            }
        });

    scopes[0]["file_delete"] = std::make_shared<BuiltinValue>("file_delete",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("file_delete expects filename");
            auto filename = dynamic_cast<StringValue*>(args[0].get());
            if (!filename) throw std::runtime_error("file_delete requires string filename");

            try {
                bool removed = std::filesystem::remove(filename->value);
                return std::make_shared<BooleanValue>(removed);
            }
            catch (...) {
                return std::make_shared<BooleanValue>(false);
            }
        });

    // System functions
    scopes[0]["exit"] = std::make_shared<BuiltinValue>("exit",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            int code = 0;
            if (args.size() > 0) {
                auto num = dynamic_cast<NumberValue*>(args[0].get());
                if (num) code = static_cast<int>(num->value);
            }
            exit(code);
            return std::make_shared<NullValue>();
        });

    scopes[0]["sleep"] = std::make_shared<BuiltinValue>("sleep",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("sleep expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("sleep requires number");
            Sleep(static_cast<DWORD>(num->value));
            return std::make_shared<NullValue>();
        });

    scopes[0]["pid"] = std::make_shared<BuiltinValue>("pid",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            return std::make_shared<NumberValue>(static_cast<double>(GetCurrentProcessId()));
        });

    scopes[0]["tid"] = std::make_shared<BuiltinValue>("tid",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            return std::make_shared<NumberValue>(static_cast<double>(GetCurrentThreadId()));
        });

    scopes[0]["time"] = std::make_shared<BuiltinValue>("time",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            return std::make_shared<NumberValue>(static_cast<double>(GetTickCount64()));
        });

    // Memory operations
    scopes[0]["memcpy"] = std::make_shared<BuiltinValue>("memcpy",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 3) throw std::runtime_error("memcpy expects 3 arguments");
            auto dest = dynamic_cast<PointerValue*>(args[0].get());
            auto src = dynamic_cast<PointerValue*>(args[1].get());
            auto size = dynamic_cast<NumberValue*>(args[2].get());
            if (!dest || !src || !size) throw std::runtime_error("memcpy requires pointers and size");
            memcpy(dest->ptr, src->ptr, static_cast<size_t>(size->value));
            return std::make_shared<NullValue>();
        });

    scopes[0]["memset"] = std::make_shared<BuiltinValue>("memset",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 3) throw std::runtime_error("memset expects 3 arguments");
            auto ptr = dynamic_cast<PointerValue*>(args[0].get());
            auto value = dynamic_cast<NumberValue*>(args[1].get());
            auto size = dynamic_cast<NumberValue*>(args[2].get());
            if (!ptr || !value || !size) throw std::runtime_error("memset requires pointer, value and size");
            memset(ptr->ptr, static_cast<int>(value->value), static_cast<size_t>(size->value));
            return std::make_shared<NullValue>();
        });

    scopes[0]["ptr"] = std::make_shared<BuiltinValue>("ptr",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("ptr expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("ptr requires number");
            void* ptr = reinterpret_cast<void*>(static_cast<uintptr_t>(num->value));
            return std::make_shared<PointerValue>(ptr);
        });

    // Type checking
    scopes[0]["type"] = std::make_shared<BuiltinValue>("type",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("type expects 1 argument");
            if (dynamic_cast<NumberValue*>(args[0].get())) return std::make_shared<StringValue>("number");
            if (dynamic_cast<StringValue*>(args[0].get())) return std::make_shared<StringValue>("string");
            if (dynamic_cast<BooleanValue*>(args[0].get())) return std::make_shared<StringValue>("boolean");
            if (dynamic_cast<ArrayValue*>(args[0].get())) return std::make_shared<StringValue>("array");
            if (dynamic_cast<DictValue*>(args[0].get())) return std::make_shared<StringValue>("dict");
            if (dynamic_cast<FunctionValue*>(args[0].get())) return std::make_shared<StringValue>("function");
            if (dynamic_cast<BuiltinValue*>(args[0].get())) return std::make_shared<StringValue>("builtin");
            if (dynamic_cast<PointerValue*>(args[0].get())) return std::make_shared<StringValue>("pointer");
            if (dynamic_cast<NullValue*>(args[0].get())) return std::make_shared<StringValue>("null");
            return std::make_shared<StringValue>("unknown");
        });

    // Input function
    scopes[0]["input"] = std::make_shared<BuiltinValue>("input",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            std::string prompt = "";
            if (args.size() > 0) prompt = args[0]->toString();
            std::cout << prompt;
            std::string input;
            std::getline(std::cin, input);
            return std::make_shared<StringValue>(input);
        });

    // Async functions
    scopes[0]["async_sleep"] = std::make_shared<BuiltinValue>("async_sleep",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("async_sleep expects 1 argument");
            auto num = dynamic_cast<NumberValue*>(args[0].get());
            if (!num) throw std::runtime_error("async_sleep requires number");

            auto promise = std::make_shared<PromiseValue>(
                std::async(std::launch::async, [ms = static_cast<DWORD>(num->value)]() -> ValuePtr {
                    Sleep(ms);
                    return std::make_shared<NullValue>();
                    })
            );
            return promise;
        });

    // Error handling
    scopes[0]["throw"] = std::make_shared<BuiltinValue>("throw",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            std::string message = "Error";
            if (args.size() > 0) {
                message = args[0]->toString();
            }
            throw std::runtime_error(message);
            return std::make_shared<NullValue>();
        });

    // Higher-order functions
    scopes[0]["map"] = std::make_shared<BuiltinValue>("map",
        [this](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("map expects 2 arguments");

            auto arr = dynamic_cast<ArrayValue*>(args[1].get());
            if (!arr) throw std::runtime_error("map requires array as second argument");

            auto result = std::make_shared<ArrayValue>();

            if (auto builtin = dynamic_cast<BuiltinValue*>(args[0].get())) {
                for (auto& elem : arr->elements) {
                    std::vector<ValuePtr> callArgs = { elem };
                    result->elements.push_back(builtin->func(callArgs));
                }
            }
            else {
                throw std::runtime_error("map currently only works with builtin functions");
            }

            return result;
        });

    scopes[0]["filter"] = std::make_shared<BuiltinValue>("filter",
        [this](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("filter expects 2 arguments");

            auto arr = dynamic_cast<ArrayValue*>(args[1].get());
            if (!arr) throw std::runtime_error("filter requires array as second argument");

            auto result = std::make_shared<ArrayValue>();

            if (auto builtin = dynamic_cast<BuiltinValue*>(args[0].get())) {
                for (auto& elem : arr->elements) {
                    std::vector<ValuePtr> callArgs = { elem };
                    auto cond = builtin->func(callArgs);
                    if (cond->isTruthy()) {
                        result->elements.push_back(elem);
                    }
                }
            }
            else {
                throw std::runtime_error("filter currently only works with builtin functions");
            }

            return result;
        });

    // DLL import function
    scopes[0]["ImportDLL"] = std::make_shared<BuiltinValue>("ImportDLL",
        [this](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() < 2 || args.size() > 3) throw std::runtime_error("ImportDLL expects 2 or 3 arguments");

            auto dllPath = dynamic_cast<StringValue*>(args[0].get());
            auto funcName = dynamic_cast<StringValue*>(args[1].get());
            if (!dllPath || !funcName) throw std::runtime_error("ImportDLL requires string arguments");

            std::string alias = funcName->value;
            if (args.size() == 3) {
                auto aliasVal = dynamic_cast<StringValue*>(args[2].get());
                if (aliasVal) alias = aliasVal->value;
            }

            HMODULE hModule = LoadLibraryA(dllPath->value.c_str());
            if (!hModule) {
                throw std::runtime_error("Failed to load DLL: " + dllPath->value);
            }

            FARPROC funcPtr = GetProcAddress(hModule, funcName->value.c_str());
            if (!funcPtr) {
                FreeLibrary(hModule);
                throw std::runtime_error("Function not found in DLL: " + funcName->value);
            }

            auto dllFunc = std::make_shared<DLLFunctionValue>(alias, funcPtr, hModule);

            dllFunc->func = [funcPtr, funcName = funcName->value](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (funcName == "MessageBoxA" && args.size() >= 4) {
                    auto text = dynamic_cast<StringValue*>(args[0].get());
                    auto caption = dynamic_cast<StringValue*>(args[1].get());
                    auto type = dynamic_cast<NumberValue*>(args[2].get());
                    auto hwnd = dynamic_cast<NumberValue*>(args[3].get());

                    if (text && caption && type && hwnd) {
                        int result = MessageBoxA(
                            reinterpret_cast<HWND>(static_cast<intptr_t>(hwnd->value)),
                            text->value.c_str(),
                            caption->value.c_str(),
                            static_cast<UINT>(type->value)
                        );
                        return std::make_shared<NumberValue>(result);
                    }
                }

                if (funcName == "GetCurrentProcessId") {
                    DWORD pid = GetCurrentProcessId();
                    return std::make_shared<NumberValue>(static_cast<double>(pid));
                }

                if (funcName == "GetCurrentThreadId") {
                    DWORD tid = GetCurrentThreadId();
                    return std::make_shared<NumberValue>(static_cast<double>(tid));
                }

                if (funcName == "GetTickCount") {
                    DWORD ticks = GetTickCount();
                    return std::make_shared<NumberValue>(static_cast<double>(ticks));
                }

                if (funcName == "Sleep") {
                    if (args.size() >= 1) {
                        auto ms = dynamic_cast<NumberValue*>(args[0].get());
                        if (ms) {
                            Sleep(static_cast<DWORD>(ms->value));
                            return std::make_shared<NullValue>();
                        }
                    }
                }

                return std::make_shared<NullValue>();
                };

            this->setVar(alias, dllFunc);
            return dllFunc;
        });

    // Process manipulation functions
    scopes[0]["find_process"] = std::make_shared<BuiltinValue>("find_process",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("find_process expects 1 argument");
            auto name = dynamic_cast<StringValue*>(args[0].get());
            if (!name) throw std::runtime_error("find_process requires string");

            unsigned long pid = ollang_find_process_id(name->value.c_str());
            return std::make_shared<NumberValue>(static_cast<double>(pid));
        });

    scopes[0]["open_process"] = std::make_shared<BuiltinValue>("open_process",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("open_process expects 2 arguments");
            auto pidVal = dynamic_cast<NumberValue*>(args[0].get());
            auto accessVal = dynamic_cast<NumberValue*>(args[1].get());
            if (!pidVal || !accessVal) throw std::runtime_error("open_process requires numbers");

            void* hProcess = ollang_open_process(
                static_cast<unsigned long>(pidVal->value),
                static_cast<unsigned long>(accessVal->value)
            );
            return std::make_shared<PointerValue>(hProcess);
        });

    scopes[0]["close_handle"] = std::make_shared<BuiltinValue>("close_handle",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("close_handle expects 1 argument");
            auto ptr = dynamic_cast<PointerValue*>(args[0].get());
            if (!ptr) throw std::runtime_error("close_handle requires pointer");

            int result = ollang_close_handle(ptr->ptr);
            return std::make_shared<BooleanValue>(result != 0);
        });

    // External memory functions
    scopes[0]["read_process_memory"] = std::make_shared<BuiltinValue>("read_process_memory",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 4) throw std::runtime_error("read_process_memory expects 4 arguments");
            auto process = dynamic_cast<PointerValue*>(args[0].get());
            auto address = dynamic_cast<PointerValue*>(args[1].get());
            auto buffer = dynamic_cast<PointerValue*>(args[2].get());
            auto size = dynamic_cast<NumberValue*>(args[3].get());
            if (!process || !address || !buffer || !size)
                throw std::runtime_error("read_process_memory requires pointers and size");

            int result = ollang_read_process_memory(
                process->ptr,
                address->ptr,
                buffer->ptr,
                static_cast<size_t>(size->value)
            );
            return std::make_shared<BooleanValue>(result != 0);
        });

    scopes[0]["write_process_memory"] = std::make_shared<BuiltinValue>("write_process_memory",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 4) throw std::runtime_error("write_process_memory expects 4 arguments");
            auto process = dynamic_cast<PointerValue*>(args[0].get());
            auto address = dynamic_cast<PointerValue*>(args[1].get());
            auto buffer = dynamic_cast<PointerValue*>(args[2].get());
            auto size = dynamic_cast<NumberValue*>(args[3].get());
            if (!process || !address || !buffer || !size)
                throw std::runtime_error("write_process_memory requires pointers and size");

            int result = ollang_write_process_memory(
                process->ptr,
                address->ptr,
                buffer->ptr,
                static_cast<size_t>(size->value)
            );
            return std::make_shared<BooleanValue>(result != 0);
        });

    // DLL injection
    scopes[0]["inject_dll"] = std::make_shared<BuiltinValue>("inject_dll",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("inject_dll expects 2 arguments");
            auto pid = dynamic_cast<NumberValue*>(args[0].get());
            auto dllPath = dynamic_cast<StringValue*>(args[1].get());
            if (!pid || !dllPath) throw std::runtime_error("inject_dll requires number and string");

            int result = ollang_inject_dll(
                static_cast<unsigned long>(pid->value),
                dllPath->value.c_str()
            );
            return std::make_shared<BooleanValue>(result != 0);
        });

    // Memory scanning
    scopes[0]["scan_memory"] = std::make_shared<BuiltinValue>("scan_memory",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 5) throw std::runtime_error("scan_memory expects 5 arguments");
            auto process = dynamic_cast<PointerValue*>(args[0].get());
            auto start = dynamic_cast<PointerValue*>(args[1].get());
            auto size = dynamic_cast<NumberValue*>(args[2].get());
            auto pattern = dynamic_cast<PointerValue*>(args[3].get());
            auto patternLen = dynamic_cast<NumberValue*>(args[4].get());
            if (!process || !start || !size || !pattern || !patternLen)
                throw std::runtime_error("scan_memory requires pointers and numbers");

            size_t result = ollang_scan_external(
                process->ptr,
                start->ptr,
                static_cast<size_t>(size->value),
                static_cast<const unsigned char*>(pattern->ptr),
                static_cast<size_t>(patternLen->value)
            );
            return std::make_shared<NumberValue>(static_cast<double>(result));
        });

    // Window functions
    scopes[0]["find_window"] = std::make_shared<BuiltinValue>("find_window",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("find_window expects 2 arguments");
            auto className = dynamic_cast<StringValue*>(args[0].get());
            auto windowName = dynamic_cast<StringValue*>(args[1].get());
            if (!className || !windowName) throw std::runtime_error("find_window requires strings");

            HWND hwnd = reinterpret_cast<HWND>(ollang_find_window(className->value.c_str(), windowName->value.c_str()));
            if (!hwnd) {
                return std::make_shared<NumberValue>(0);
            }
            return std::make_shared<NumberValue>(static_cast<double>(reinterpret_cast<uintptr_t>(hwnd)));
        });

    scopes[0]["get_window_pid"] = std::make_shared<BuiltinValue>("get_window_pid",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("get_window_pid expects 1 argument");
            auto hwndVal = dynamic_cast<NumberValue*>(args[0].get());
            if (!hwndVal) throw std::runtime_error("get_window_pid requires number");

            HWND hwnd = reinterpret_cast<HWND>(static_cast<uintptr_t>(hwndVal->value));
            unsigned long pid = ollang_get_window_process_id(hwnd);
            return std::make_shared<NumberValue>(static_cast<double>(pid));
        });

    // Thread functions
    scopes[0]["create_thread"] = std::make_shared<BuiltinValue>("create_thread",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("create_thread expects 2 arguments");
            auto startAddress = dynamic_cast<PointerValue*>(args[0].get());
            auto parameter = dynamic_cast<PointerValue*>(args[1].get());
            if (!startAddress || !parameter) throw std::runtime_error("create_thread requires pointers");

            void* thread = ollang_create_thread(startAddress->ptr, parameter->ptr);
            return std::make_shared<PointerValue>(thread);
        });

    scopes[0]["suspend_thread"] = std::make_shared<BuiltinValue>("suspend_thread",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("suspend_thread expects 1 argument");
            auto thread = dynamic_cast<PointerValue*>(args[0].get());
            if (!thread) throw std::runtime_error("suspend_thread requires pointer");

            int result = ollang_suspend_thread(thread->ptr);
            return std::make_shared<BooleanValue>(result != 0);
        });

    scopes[0]["resume_thread"] = std::make_shared<BuiltinValue>("resume_thread",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 1) throw std::runtime_error("resume_thread expects 1 argument");
            auto thread = dynamic_cast<PointerValue*>(args[0].get());
            if (!thread) throw std::runtime_error("resume_thread requires pointer");

            int result = ollang_resume_thread(thread->ptr);
            return std::make_shared<BooleanValue>(result != 0);
        });

    // Hooking functions
    scopes[0]["write_jmp"] = std::make_shared<BuiltinValue>("write_jmp",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("write_jmp expects 2 arguments");
            auto target = dynamic_cast<PointerValue*>(args[0].get());
            auto destination = dynamic_cast<PointerValue*>(args[1].get());
            if (!target || !destination) throw std::runtime_error("write_jmp requires pointers");

            int result = ollang_write_jmp(target->ptr, destination->ptr);
            return std::make_shared<BooleanValue>(result != 0);
        });

    scopes[0]["write_call"] = std::make_shared<BuiltinValue>("write_call",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() != 2) throw std::runtime_error("write_call expects 2 arguments");
            auto target = dynamic_cast<PointerValue*>(args[0].get());
            auto destination = dynamic_cast<PointerValue*>(args[1].get());
            if (!target || !destination) throw std::runtime_error("write_call requires pointers");

            int result = ollang_write_call(target->ptr, destination->ptr);
            return std::make_shared<BooleanValue>(result != 0);
        });
    // HTTP Implementation
    scopes[0]["http_get"] = std::make_shared<BuiltinValue>("http_get",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.empty()) throw std::runtime_error("http_get requires URL");
            auto url = dynamic_cast<StringValue*>(args[0].get());
            if (!url) throw std::runtime_error("http_get requires STRING URL");

            HttpRequest req;
            req.method = "GET";
            req.url = url->value;
            req.timeout_ms = 30000;
            req.verify_ssl = true;

            HttpResponse resp;
            resp.success = false;
            resp.status_code = 0;

            CURL* curl = curl_easy_init();
            if (!curl) throw std::runtime_error("Failed to initialize CURL");

            auto writeCallback = +[](void* contents, size_t size, size_t nmemb, void* userp) -> size_t {
                size_t totalSize = size * nmemb;
                ((std::string*)userp)->append((char*)contents, totalSize);
                return totalSize;
                };

            curl_easy_setopt(curl, CURLOPT_URL, req.url.c_str());
            curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, writeCallback);
            curl_easy_setopt(curl, CURLOPT_WRITEDATA, &resp.body);
            curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, (long)req.timeout_ms);

            if (!req.verify_ssl) {
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 0L);
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 0L);
            }

            CURLcode res = curl_easy_perform(curl);

            if (res != CURLE_OK) {
                resp.error = curl_easy_strerror(res);
                resp.success = false;
                curl_easy_cleanup(curl);
                throw std::runtime_error("HTTP GET failed: " + resp.error);
            }

            long status_code;
            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &status_code);
            resp.status_code = (int)status_code;
            resp.success = true;

            curl_easy_cleanup(curl);
            return std::make_shared<StringValue>(resp.body);
        }
    );

    scopes[0]["http_post"] = std::make_shared<BuiltinValue>("http_post",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() < 2) throw std::runtime_error("http_post requires URL and data");
            auto url = dynamic_cast<StringValue*>(args[0].get());
            auto data = dynamic_cast<StringValue*>(args[1].get());
            if (!url || !data) throw std::runtime_error("http_post requires STRING URL and STRING data");

            HttpRequest req;
            req.method = "POST";
            req.url = url->value;
            req.body = data->value;
            req.timeout_ms = 30000;
            req.verify_ssl = true;

            HttpResponse resp;
            resp.success = false;
            resp.status_code = 0;

            CURL* curl = curl_easy_init();
            if (!curl) throw std::runtime_error("Failed to initialize CURL");

            auto writeCallback = +[](void* contents, size_t size, size_t nmemb, void* userp) -> size_t {
                size_t totalSize = size * nmemb;
                ((std::string*)userp)->append((char*)contents, totalSize);
                return totalSize;
                };

            curl_easy_setopt(curl, CURLOPT_URL, req.url.c_str());
            curl_easy_setopt(curl, CURLOPT_POST, 1L);
            curl_easy_setopt(curl, CURLOPT_POSTFIELDS, req.body.c_str());
            curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, writeCallback);
            curl_easy_setopt(curl, CURLOPT_WRITEDATA, &resp.body);
            curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, (long)req.timeout_ms);

            if (!req.verify_ssl) {
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 0L);
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 0L);
            }

            CURLcode res = curl_easy_perform(curl);

            if (res != CURLE_OK) {
                resp.error = curl_easy_strerror(res);
                resp.success = false;
                curl_easy_cleanup(curl);
                throw std::runtime_error("HTTP POST failed: " + resp.error);
            }

            long status_code;
            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &status_code);
            resp.status_code = (int)status_code;
            resp.success = true;

            curl_easy_cleanup(curl);
            return std::make_shared<StringValue>(resp.body);
        }
    );

    scopes[0]["http_put"] = std::make_shared<BuiltinValue>("http_put",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.size() < 2) throw std::runtime_error("http_put requires URL and data");
            auto url = dynamic_cast<StringValue*>(args[0].get());
            auto data = dynamic_cast<StringValue*>(args[1].get());
            if (!url || !data) throw std::runtime_error("http_put requires STRING URL and STRING data");

            HttpRequest req;
            req.method = "PUT";
            req.url = url->value;
            req.body = data->value;
            req.timeout_ms = 30000;
            req.verify_ssl = true;

            HttpResponse resp;
            resp.success = false;
            resp.status_code = 0;

            CURL* curl = curl_easy_init();
            if (!curl) throw std::runtime_error("Failed to initialize CURL");

            auto writeCallback = +[](void* contents, size_t size, size_t nmemb, void* userp) -> size_t {
                size_t totalSize = size * nmemb;
                ((std::string*)userp)->append((char*)contents, totalSize);
                return totalSize;
                };

            curl_easy_setopt(curl, CURLOPT_URL, req.url.c_str());
            curl_easy_setopt(curl, CURLOPT_CUSTOMREQUEST, "PUT");
            curl_easy_setopt(curl, CURLOPT_POSTFIELDS, req.body.c_str());
            curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, writeCallback);
            curl_easy_setopt(curl, CURLOPT_WRITEDATA, &resp.body);
            curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, (long)req.timeout_ms);

            if (!req.verify_ssl) {
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 0L);
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 0L);
            }

            CURLcode res = curl_easy_perform(curl);

            if (res != CURLE_OK) {
                resp.error = curl_easy_strerror(res);
                resp.success = false;
                curl_easy_cleanup(curl);
                throw std::runtime_error("HTTP PUT failed: " + resp.error);
            }

            long status_code;
            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &status_code);
            resp.status_code = (int)status_code;
            resp.success = true;

            curl_easy_cleanup(curl);
            return std::make_shared<StringValue>(resp.body);
        }
    );

    scopes[0]["http_delete"] = std::make_shared<BuiltinValue>("http_delete",
        [](const std::vector<ValuePtr>& args) -> ValuePtr {
            if (args.empty()) throw std::runtime_error("http_delete requires URL");
            auto url = dynamic_cast<StringValue*>(args[0].get());
            if (!url) throw std::runtime_error("http_delete requires STRING URL");

            HttpRequest req;
            req.method = "DELETE";
            req.url = url->value;
            req.timeout_ms = 30000;
            req.verify_ssl = true;

            HttpResponse resp;
            resp.success = false;
            resp.status_code = 0;

            CURL* curl = curl_easy_init();
            if (!curl) throw std::runtime_error("Failed to initialize CURL");

            auto writeCallback = +[](void* contents, size_t size, size_t nmemb, void* userp) -> size_t {
                size_t totalSize = size * nmemb;
                ((std::string*)userp)->append((char*)contents, totalSize);
                return totalSize;
                };

            curl_easy_setopt(curl, CURLOPT_URL, req.url.c_str());
            curl_easy_setopt(curl, CURLOPT_CUSTOMREQUEST, "DELETE");
            curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, writeCallback);
            curl_easy_setopt(curl, CURLOPT_WRITEDATA, &resp.body);
            curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, (long)req.timeout_ms);

            if (!req.verify_ssl) {
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 0L);
                curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 0L);
            }

            CURLcode res = curl_easy_perform(curl);

            if (res != CURLE_OK) {
                resp.error = curl_easy_strerror(res);
                resp.success = false;
                curl_easy_cleanup(curl);
                throw std::runtime_error("HTTP DELETE failed: " + resp.error);
            }

            long status_code;
            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &status_code);
            resp.status_code = (int)status_code;
            resp.success = true;

            curl_easy_cleanup(curl);
            return std::make_shared<StringValue>(resp.body);
        }
    );

    // Initialize stdlib (ollang standard library from stdlib.cpp)
    InitStdLib(*this);
}
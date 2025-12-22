#include "ollang.h"
#include "ollang_c.h"
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include <cstring>

// Value implementations
NumberValue::NumberValue(double v) : value(v) {}

std::string NumberValue::toString() const {
    std::stringstream ss;
    if (value == static_cast<int64_t>(value)) {
        ss << static_cast<int64_t>(value);
    }
    else {
        ss << std::fixed << std::setprecision(6) << value;
        std::string s = ss.str();
        while (!s.empty() && (s.back() == '0' || s.back() == '.')) {
            if (s.back() == '.') {
                s.pop_back();
                break;
            }
            s.pop_back();
        }
        return s;
    }
    return ss.str();
}

bool NumberValue::isTruthy() const {
    return value != 0;
}

StringValue::StringValue(const std::string& v) : value(v) {}

std::string StringValue::toString() const { return value; }

bool StringValue::isTruthy() const { return !value.empty(); }

BooleanValue::BooleanValue(bool v) : value(v) {}

std::string BooleanValue::toString() const { return value ? "true" : "false"; }

bool BooleanValue::isTruthy() const { return value; }

std::string NullValue::toString() const { return "null"; }

bool NullValue::isTruthy() const { return false; }

std::string ArrayValue::toString() const {
    std::string r = "[";
    for (size_t i = 0; i < elements.size(); i++) {
        if (i > 0) r += ", ";
        r += elements[i]->toString();
    }
    return r + "]";
}

std::string DictValue::toString() const {
    std::string r = "{";
    bool first = true;
    for (const auto& [k, v] : items) {
        if (!first) r += ", ";
        r += k + ": " + v->toString();
        first = false;
    }
    return r + "}";
}

FunctionValue::FunctionValue(const std::string& n, const std::vector<std::string>& p,
    const std::vector<std::shared_ptr<Node>>& b)
    : name(n), params(p), body(b) {
}

std::string FunctionValue::toString() const {
    return "<function " + name + ">";
}

std::string BuiltinValue::toString() const {
    return "<builtin " + name + ">";
}

BuiltinValue::BuiltinValue(const std::string& n, BuiltinFunc f)
    : name(n), func(f) {
}

PointerValue::PointerValue(void* p, size_t s, bool o)
    : ptr(p), size(s), owned(o) {
}

std::string PointerValue::toString() const {
    std::stringstream ss;
    ss << "0x" << std::hex << reinterpret_cast<uintptr_t>(ptr);
    if (size > 0) {
        ss << " [" << std::dec << size << " bytes]";
    }
    return ss.str();
}

bool PointerValue::isTruthy() const {
    return ptr != nullptr;
}

PointerValue::~PointerValue() {
    if (owned && ptr) {
        ollang_free(ptr);
    }
}

Token::Token(const std::string& t, const std::variant<std::string, double>& v, int l, int c)
    : type(t), value(v), line(l), col(c) {
}
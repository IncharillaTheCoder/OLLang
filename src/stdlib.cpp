#define _CRT_SECURE_NO_WARNINGS
#define NOMINMAX

#include "ollang.h"
#include <iostream>
#include <vector>
#include <string>
#include <map>
#include <algorithm>
#include <cmath>
#include <ctime>
#include <random>
#include <sstream>
#include <fstream>
#include <filesystem>
#include <chrono>
#include <cstdlib>
#include <cstdio>
#include <functional>
#include <cctype>
#include <windows.h>

namespace StdLib {

    // Math functions
    double abs(double x) { return std::abs(x); }
    double sqrt(double x) { return std::sqrt(x); }
    double pow(double x, double y) { return std::pow(x, y); }
    double sin(double x) { return std::sin(x); }
    double cos(double x) { return std::cos(x); }
    double tan(double x) { return std::tan(x); }
    double log(double x) { return std::log(x); }
    double log10(double x) { return std::log10(x); }
    double exp(double x) { return std::exp(x); }
    double floor(double x) { return std::floor(x); }
    double ceil(double x) { return std::ceil(x); }
    double round(double x) { return std::round(x); }
    double max(double a, double b) { return (std::max)(a, b); }
    double min(double a, double b) { return (std::min)(a, b); }

    const double PI = 3.14159265358979323846;
    const double E = 2.71828182845904523536;

    static std::mt19937& get_rng() {
        static std::mt19937 rng(static_cast<unsigned int>(std::time(nullptr)));
        return rng;
    }

    double random() {
        std::uniform_real_distribution<double> dist(0.0, 1.0);
        return dist(get_rng());
    }

    int randomInt(int min, int max) {
        std::uniform_int_distribution<int> dist(min, max);
        return dist(get_rng());
    }

    // String functions
    std::string toUpper(const std::string& str) {
        std::string result = str;
        std::transform(result.begin(), result.end(), result.begin(),
            [](unsigned char c) { return static_cast<char>(std::toupper(c)); });
        return result;
    }

    std::string toLower(const std::string& str) {
        std::string result = str;
        std::transform(result.begin(), result.end(), result.begin(),
            [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return result;
    }

    std::string trim(const std::string& str) {
        std::string result = str;
        result.erase(result.begin(),
            std::find_if(result.begin(), result.end(),
                [](unsigned char c) { return !std::isspace(c); }));
        result.erase(
            std::find_if(result.rbegin(), result.rend(),
                [](unsigned char c) { return !std::isspace(c); }).base(),
            result.end());
        return result;
    }

    std::vector<std::string> split(const std::string& str, const std::string& delimiter) {
        std::vector<std::string> result;
        if (delimiter.empty()) {
            for (char c : str) result.emplace_back(1, c);
            return result;
        }

        size_t start = 0;
        size_t pos;
        while ((pos = str.find(delimiter, start)) != std::string::npos) {
            result.push_back(str.substr(start, pos - start));
            start = pos + delimiter.length();
        }
        result.push_back(str.substr(start));
        return result;
    }

    std::string replace(const std::string& str, const std::string& from, const std::string& to) {
        if (from.empty()) return str;
        std::string result = str;
        size_t pos = 0;
        while ((pos = result.find(from, pos)) != std::string::npos) {
            result.replace(pos, from.length(), to);
            pos += to.length();
        }
        return result;
    }

    std::string substr(const std::string& str, int start, int length = -1) {
        int size = static_cast<int>(str.size());
        if (start < 0) start += size;
        if (start < 0) start = 0;
        if (start >= size) return "";

        if (length < 0 || start + length > size)
            length = size - start;

        return str.substr(start, length);
    }

    // Array functions
    std::vector<ValuePtr> slice(const std::vector<ValuePtr>& arr, int start, int end = -1) {
        int size = static_cast<int>(arr.size());
        if (end < 0) end = size;

        if (start < 0) start += size;
        if (end < 0) end += size;

        start = (std::max)(0, start);
        end = (std::min)(size, end);

        if (start >= end) return {};

        return std::vector<ValuePtr>(arr.begin() + start, arr.begin() + end);
    }

    // File system functions
    bool fileExists(const std::string& path) {
        return std::filesystem::exists(path);
    }

    std::string readFile(const std::string& path) {
        std::ifstream file(path, std::ios::binary);
        if (!file) return "";
        return std::string(
            (std::istreambuf_iterator<char>(file)),
            std::istreambuf_iterator<char>());
    }

    bool writeFile(const std::string& path, const std::string& content) {
        std::ofstream file(path, std::ios::binary);
        if (!file) return false;
        file << content;
        return true;
    }

    bool appendFile(const std::string& path, const std::string& content) {
        std::ofstream file(path, std::ios::app | std::ios::binary);
        if (!file) return false;
        file << content;
        return true;
    }

    bool deleteFile(const std::string& path) {
        try {
            return std::filesystem::remove(path);
        }
        catch (...) {
            return false;
        }
    }

    // System functions
    std::string time(const std::string& format = "") {
        time_t now = ::time(nullptr);
        tm timeinfo{};
        localtime_s(&timeinfo, &now);

        char buffer[256];
        const char* fmt = format.empty() ? "%Y-%m-%d %H:%M:%S" : format.c_str();
        strftime(buffer, sizeof(buffer), fmt, &timeinfo);
        return buffer;
    }

    long long timestamp() {
        return static_cast<long long>(::time(nullptr));
    }

    long long timestampMs() {
        auto now = std::chrono::system_clock::now();
        return std::chrono::duration_cast<std::chrono::milliseconds>(
            now.time_since_epoch()).count();
    }

    void sleep(int milliseconds) {
        Sleep(milliseconds);
    }

    std::string platform() {
        return "Windows";
    }

    int pid() {
        return static_cast<int>(GetCurrentProcessId());
    }

    int tid() {
        return static_cast<int>(GetCurrentThreadId());
    }
}

void InitStdLib(Interpreter& interpreter) {

    // MATH FUNCTIONS
    interpreter.setVar("abs",
        std::make_shared<BuiltinValue>("abs",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("abs expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("abs requires number");
                return std::make_shared<NumberValue>(StdLib::abs(n->value));
            }));

    interpreter.setVar("sqrt",
        std::make_shared<BuiltinValue>("sqrt",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("sqrt expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("sqrt requires number");
                return std::make_shared<NumberValue>(StdLib::sqrt(n->value));
            }));

    interpreter.setVar("pow",
        std::make_shared<BuiltinValue>("pow",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 2) throw std::runtime_error("pow expects 2 arguments");
                auto base = dynamic_cast<NumberValue*>(args[0].get());
                auto exp = dynamic_cast<NumberValue*>(args[1].get());
                if (!base || !exp) throw std::runtime_error("pow requires numbers");
                return std::make_shared<NumberValue>(StdLib::pow(base->value, exp->value));
            }));

    interpreter.setVar("max",
        std::make_shared<BuiltinValue>("max",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 2) throw std::runtime_error("max expects 2 arguments");
                auto a = dynamic_cast<NumberValue*>(args[0].get());
                auto b = dynamic_cast<NumberValue*>(args[1].get());
                if (!a || !b) throw std::runtime_error("max requires numbers");
                return std::make_shared<NumberValue>(StdLib::max(a->value, b->value));
            }));

    interpreter.setVar("min",
        std::make_shared<BuiltinValue>("min",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 2) throw std::runtime_error("min expects 2 arguments");
                auto a = dynamic_cast<NumberValue*>(args[0].get());
                auto b = dynamic_cast<NumberValue*>(args[1].get());
                if (!a || !b) throw std::runtime_error("min requires numbers");
                return std::make_shared<NumberValue>(StdLib::min(a->value, b->value));
            }));

    interpreter.setVar("floor",
        std::make_shared<BuiltinValue>("floor",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("floor expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("floor requires number");
                return std::make_shared<NumberValue>(StdLib::floor(n->value));
            }));

    interpreter.setVar("ceil",
        std::make_shared<BuiltinValue>("ceil",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("ceil expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("ceil requires number");
                return std::make_shared<NumberValue>(StdLib::ceil(n->value));
            }));

    interpreter.setVar("round",
        std::make_shared<BuiltinValue>("round",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("round expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("round requires number");
                return std::make_shared<NumberValue>(StdLib::round(n->value));
            }));

    interpreter.setVar("sin",
        std::make_shared<BuiltinValue>("sin",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("sin expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("sin requires number");
                return std::make_shared<NumberValue>(StdLib::sin(n->value));
            }));

    interpreter.setVar("cos",
        std::make_shared<BuiltinValue>("cos",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("cos expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("cos requires number");
                return std::make_shared<NumberValue>(StdLib::cos(n->value));
            }));

    interpreter.setVar("tan",
        std::make_shared<BuiltinValue>("tan",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("tan expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("tan requires number");
                return std::make_shared<NumberValue>(StdLib::tan(n->value));
            }));

    interpreter.setVar("random",
        std::make_shared<BuiltinValue>("random",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() == 0) {
                    return std::make_shared<NumberValue>(StdLib::random());
                }
                else if (args.size() == 2) {
                    auto min = dynamic_cast<NumberValue*>(args[0].get());
                    auto max = dynamic_cast<NumberValue*>(args[1].get());
                    if (!min || !max) throw std::runtime_error("random requires numbers");
                    return std::make_shared<NumberValue>(StdLib::randomInt(
                        static_cast<int>(min->value),
                        static_cast<int>(max->value)));
                }
                throw std::runtime_error("random expects 0 or 2 arguments");
            }));

    // STRING FUNCTIONS
    interpreter.setVar("upper",
        std::make_shared<BuiltinValue>("upper",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("upper expects 1 argument");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                if (!s) throw std::runtime_error("upper requires string");
                return std::make_shared<StringValue>(StdLib::toUpper(s->value));
            }));

    interpreter.setVar("lower",
        std::make_shared<BuiltinValue>("lower",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("lower expects 1 argument");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                if (!s) throw std::runtime_error("lower requires string");
                return std::make_shared<StringValue>(StdLib::toLower(s->value));
            }));

    interpreter.setVar("trim",
        std::make_shared<BuiltinValue>("trim",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("trim expects 1 argument");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                if (!s) throw std::runtime_error("trim requires string");
                return std::make_shared<StringValue>(StdLib::trim(s->value));
            }));

    interpreter.setVar("split",
        std::make_shared<BuiltinValue>("split",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 2) throw std::runtime_error("split expects 2 arguments");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                auto d = dynamic_cast<StringValue*>(args[1].get());
                if (!s || !d) throw std::runtime_error("split requires strings");

                auto arr = std::make_shared<ArrayValue>();
                auto parts = StdLib::split(s->value, d->value);
                for (const auto& p : parts)
                    arr->elements.push_back(std::make_shared<StringValue>(p));
                return arr;
            }));

    interpreter.setVar("replace",
        std::make_shared<BuiltinValue>("replace",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 3) throw std::runtime_error("replace expects 3 arguments");
                auto str = dynamic_cast<StringValue*>(args[0].get());
                auto from = dynamic_cast<StringValue*>(args[1].get());
                auto to = dynamic_cast<StringValue*>(args[2].get());
                if (!str || !from || !to) throw std::runtime_error("replace requires strings");
                return std::make_shared<StringValue>(StdLib::replace(str->value, from->value, to->value));
            }));

    interpreter.setVar("substr",
        std::make_shared<BuiltinValue>("substr",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() < 2 || args.size() > 3) throw std::runtime_error("substr expects 2 or 3 arguments");
                auto str = dynamic_cast<StringValue*>(args[0].get());
                auto start = dynamic_cast<NumberValue*>(args[1].get());
                if (!str || !start) throw std::runtime_error("substr requires string and number");

                if (args.size() == 3) {
                    auto length = dynamic_cast<NumberValue*>(args[2].get());
                    if (!length) throw std::runtime_error("substr third argument must be number");
                    return std::make_shared<StringValue>(StdLib::substr(str->value,
                        static_cast<int>(start->value),
                        static_cast<int>(length->value)));
                }
                else {
                    return std::make_shared<StringValue>(StdLib::substr(str->value,
                        static_cast<int>(start->value)));
                }
            }));

    // ARRAY FUNCTIONS
    interpreter.setVar("slice",
        std::make_shared<BuiltinValue>("slice",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() < 2 || args.size() > 3) throw std::runtime_error("slice expects 2 or 3 arguments");
                auto arr = dynamic_cast<ArrayValue*>(args[0].get());
                auto start = dynamic_cast<NumberValue*>(args[1].get());
                if (!arr || !start) throw std::runtime_error("slice requires array and number");

                int end = -1;
                if (args.size() == 3) {
                    auto endVal = dynamic_cast<NumberValue*>(args[2].get());
                    if (!endVal) throw std::runtime_error("slice third argument must be number");
                    end = static_cast<int>(endVal->value);
                }

                auto result = std::make_shared<ArrayValue>();
                result->elements = StdLib::slice(arr->elements,
                    static_cast<int>(start->value), end);
                return result;
            }));

    // FILE SYSTEM FUNCTIONS
    interpreter.setVar("fileExists",
        std::make_shared<BuiltinValue>("fileExists",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("fileExists expects 1 argument");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                if (!s) throw std::runtime_error("fileExists requires string");
                return std::make_shared<BooleanValue>(StdLib::fileExists(s->value));
            }));

    interpreter.setVar("readFile",
        std::make_shared<BuiltinValue>("readFile",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("readFile expects 1 argument");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                if (!s) throw std::runtime_error("readFile requires string");
                return std::make_shared<StringValue>(StdLib::readFile(s->value));
            }));

    interpreter.setVar("writeFile",
        std::make_shared<BuiltinValue>("writeFile",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 2) throw std::runtime_error("writeFile expects 2 arguments");
                auto path = dynamic_cast<StringValue*>(args[0].get());
                auto content = dynamic_cast<StringValue*>(args[1].get());
                if (!path || !content) throw std::runtime_error("writeFile requires strings");
                return std::make_shared<BooleanValue>(StdLib::writeFile(path->value, content->value));
            }));

    interpreter.setVar("appendFile",
        std::make_shared<BuiltinValue>("appendFile",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 2) throw std::runtime_error("appendFile expects 2 arguments");
                auto path = dynamic_cast<StringValue*>(args[0].get());
                auto content = dynamic_cast<StringValue*>(args[1].get());
                if (!path || !content) throw std::runtime_error("appendFile requires strings");
                return std::make_shared<BooleanValue>(StdLib::appendFile(path->value, content->value));
            }));

    interpreter.setVar("deleteFile",
        std::make_shared<BuiltinValue>("deleteFile",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("deleteFile expects 1 argument");
                auto s = dynamic_cast<StringValue*>(args[0].get());
                if (!s) throw std::runtime_error("deleteFile requires string");
                return std::make_shared<BooleanValue>(StdLib::deleteFile(s->value));
            }));

    // SYSTEM FUNCTIONS
    interpreter.setVar("time",
        std::make_shared<BuiltinValue>("time",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                std::string fmt;
                if (!args.empty()) {
                    auto s = dynamic_cast<StringValue*>(args[0].get());
                    if (s) fmt = s->value;
                }
                return std::make_shared<StringValue>(StdLib::time(fmt));
            }));

    interpreter.setVar("timestamp",
        std::make_shared<BuiltinValue>("timestamp",
            [](const std::vector<ValuePtr>&) -> ValuePtr {
                return std::make_shared<NumberValue>(StdLib::timestamp());
            }));

    interpreter.setVar("sleep",
        std::make_shared<BuiltinValue>("sleep",
            [](const std::vector<ValuePtr>& args) -> ValuePtr {
                if (args.size() != 1) throw std::runtime_error("sleep expects 1 argument");
                auto n = dynamic_cast<NumberValue*>(args[0].get());
                if (!n) throw std::runtime_error("sleep requires number");
                StdLib::sleep(static_cast<int>(n->value));
                return std::make_shared<NullValue>();
            }));

    interpreter.setVar("pid",
        std::make_shared<BuiltinValue>("pid",
            [](const std::vector<ValuePtr>&) -> ValuePtr {
                return std::make_shared<NumberValue>(StdLib::pid());
            }));

    // CONSTANTS
    interpreter.setVar("PI", std::make_shared<NumberValue>(StdLib::PI));
    interpreter.setVar("E", std::make_shared<NumberValue>(StdLib::E));
}
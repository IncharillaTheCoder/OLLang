#include "ollang.h"
#include <unordered_set>
#include <cctype>

char Lexer::current() const {
    return pos < source.length() ? source[pos] : '\0';
}

char Lexer::peek(size_t offset) const {
    return pos + offset < source.length() ? source[pos + offset] : '\0';
}

void Lexer::advance() {
    if (current() == '\n') {
        line++;
        col = 1;
    }
    else {
        col++;
    }
    pos++;
}

void Lexer::skipWhitespace() {
    while (isspace(current())) advance();
}

void Lexer::skipComment() {
    if (current() == '#') {
        while (current() != '\n' && current() != '\0') advance();
    }
}

double Lexer::readNumber() {
    std::string num;
    while (isdigit(current()) || current() == '.') {
        num += current();
        advance();
    }
    return std::stod(num);
}

std::string Lexer::readString() {
    std::string str;
    advance(); // Skip opening quote
    while (current() != '"' && current() != '\0') {
        if (current() == '\\') {
            advance();
            switch (current()) {
            case 'n': str += '\n'; break;
            case 't': str += '\t'; break;
            case 'r': str += '\r'; break;
            case '"': str += '"'; break;
            case '\\': str += '\\'; break;
            default: str += current(); break;
            }
        }
        else {
            str += current();
        }
        advance();
    }
    advance(); // Skip closing quote
    return str;
}

std::string Lexer::readIdentifier() {
    std::string id;
    while (isalnum(current()) || current() == '_') {
        id += current();
        advance();
    }
    return id;
}

Lexer::Lexer(const std::string& src) : source(src) {}

std::vector<Token> Lexer::tokenize() {
    std::vector<Token> tokens;
    std::unordered_set<std::string> keywords = {
        "func", "if", "else", "while", "for", "in", "return",
        "true", "false", "null", "alloc", "free", "read", "write", "syscall",
        "import", "ImportDLL", "try", "catch", "async", "await", "throw", "namespace",
        "process", "module", "inject", "hook", "scan", "window", "thread",
        "httpGet", "httpPost", "httpPut", "httpDelete",
        "base64Encode", "base64Decode", "base64EncodeFile", "base64DecodeFile",
        "base64UrlEncode", "base64UrlDecode", "UUID", // Uniquie User Identifier
        "checkapp", "getapppid", "waitforapp", "waitforappclose", "killapp", "startapp",
        "getAvailableMemory"
    };

    while (current() != '\0') {
        skipWhitespace();
        skipComment();
        if (current() == '\0') break;

        int currentLine = line;
        int currentCol = col;
        char c = current();

        // Handle numbers
        if (isdigit(c)) {
            double num = readNumber();
            tokens.push_back(Token("number", num, currentLine, currentCol));
        }
        // Handle strings
        else if (c == '"') {
            std::string str = readString();
            tokens.push_back(Token("string", str, currentLine, currentCol));
        }
        // Handle identifiers and keywords
        else if (isalpha(c) || c == '_') {
            std::string id = readIdentifier();
            std::string type = keywords.count(id) ? "keyword" : "identifier";
            tokens.push_back(Token(type, id, currentLine, currentCol));
        }
        // Handle power operator **
        else if (c == '*' && peek() == '*') {
            advance(); advance();
            tokens.push_back(Token("operator", "**", currentLine, currentCol));
        }
        // Handle shift operators << >>
        else if ((c == '<' || c == '>') && peek() == c) {
            std::string op(2, c);
            advance(); advance();
            tokens.push_back(Token("operator", op, currentLine, currentCol));
        }
        // Handle two-character operators (==, !=, <=, >=, &&, ||)
        else if (std::string("=<>!&|").find(c) != std::string::npos && peek() == '=') {
            std::string op = std::string(1, c) + std::string(1, peek());
            advance(); advance();
            tokens.push_back(Token("operator", op, currentLine, currentCol));
        }
        // Handle logical operators && ||
        else if ((c == '&' && peek() == '&') || (c == '|' && peek() == '|')) {
            std::string op(2, c);
            advance(); advance();
            tokens.push_back(Token("operator", op, currentLine, currentCol));
        }
        // Handle single-character operators
        else if (std::string("+-*/%<>=!&|^~").find(c) != std::string::npos) {
            std::string op(1, c);
            tokens.push_back(Token("operator", op, currentLine, currentCol));
            advance();
        }
        // Handle punctuation including colon
        else if (std::string("(){}[],:;.").find(c) != std::string::npos) {
            std::string punc(1, c);
            tokens.push_back(Token("punctuation", punc, currentLine, currentCol));
            advance();
        }
        else {
            advance();
        }
    }
    return tokens;
}
#include "ollang.h"
#include <fstream>
#include <iostream>
#include <sstream>

// yes yes i know this is horribly coded but im just lazy and want it to work for now
// i mean if you really think about it all code is horribly coded #cope
// also make sure to do "vcpkg.exe install curl"
// when setting up ollang otherwise http functions will not work

std::string runOLLang(const std::string& source, Interpreter& interpreter) {
    Lexer lexer(source);
    auto tokens = lexer.tokenize();
    Parser parser(tokens);
    auto ast = parser.parse();
    return interpreter.run(ast);
}

void runRepl() {
    std::cout << "OLLang REPL (Type 'exit' to quit)" << std::endl;
    std::string line;
    Interpreter interpreter;

    while (true) {
        std::cout << ">>> ";
        if (!std::getline(std::cin, line) || line == "exit") break;
        if (line.empty()) continue;

        try {
            auto result = runOLLang(line, interpreter);

            for (const auto& out : interpreter.getOutput()) {
                std::cout << out << std::endl;
            }
            interpreter.clearOutput();

            if (!result.empty() && result != "null") {
                std::cout << result << std::endl;
            }
        }
        catch (const std::exception& e) {
            std::cout << "Error: " << e.what() << std::endl;
        }
    }
}

int main(int argc, char* argv[]) {
    try {
        if (argc < 2) {
            std::cout << "Usage:" << std::endl;
            std::cout << "  " << argv[0] << " <file.oll>    Run a script" << std::endl;
            std::cout << "  " << argv[0] << " -e \"code\"     Execute code" << std::endl;
            std::cout << "  " << argv[0] << " -repl         Start REPL" << std::endl;
            std::cout << std::endl;
            std::cout << "Examples:" << std::endl;
            std::cout << "  " << argv[0] << " script.oll" << std::endl;
            std::cout << "  " << argv[0] << " -e \"println(\\\"Hello\\\")\"" << std::endl;
            return 0;
        }

        std::string source;
		
		// the repl is 100% fucked but whatever no1 is gonna use it anyways!
        if (std::string(argv[1]) == "-repl") {
            runRepl();
            return 0;
        }
        else if (std::string(argv[1]) == "-e" && argc > 2) {
            source = argv[2];
        }
        else {
            std::ifstream file(argv[1]);
            if (!file.is_open()) {
                std::cerr << "Error opening file: " << argv[1] << std::endl;
                return 1;
            }
            std::stringstream buffer;
            buffer << file.rdbuf();
            source = buffer.str();
        }

        Interpreter interpreter;
        std::string result = runOLLang(source, interpreter);

        for (const auto& outputLine : interpreter.getOutput()) {
            std::cout << outputLine << std::endl;
        }

        if (!result.empty() && result.find("Error") != std::string::npos) {
            std::cout << result << std::endl;
        }
    }
    catch (const std::exception& e) {
        std::cerr << "Fatal Error: " << e.what() << std::endl;
        return 1;
	}
    catch (...) {
        std::cerr << "Fatal Error: Unknown exception occurred\n" << std::endl;
    }
    return 0;

}

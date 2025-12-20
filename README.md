**yes ik its horribly coded dont bully me**
# Ollang - A Low-Level Scripting Language

**Ollang** is basically python and c if they had a baby (also a bit of js)

## Features

### Core Language
- **C-like syntax** with familiar operators and control structures
- **Dynamic typing** with support for numbers, strings, booleans, arrays, dictionaries
- **Functions** with closures and lexical scoping
- **Object-oriented features** including dot notation and member access
- **Exception handling** with try/catch blocks
- **Async/await** support for asynchronous programming
- **List comprehensions** for functional-style data transformation

### Low-Level Capabilities
- **Direct memory manipulation** (allocate, free, read, write)
- **Pointer arithmetic** and raw memory access
- **System calls** for direct OS interaction
- **DLL loading** and function importing at runtime
- **External process memory** reading/writing
- **Pattern scanning** in memory
- **Thread manipulation** and hooking

### Windows Integration
- **Windows API access** via built-in functions
- **Process injection** and DLL loading
- **Window manipulation** and input simulation
- **Keyboard/mouse input** automation
- **System information** retrieval
- **Debugging utilities**

### Standard Library
- **Mathematical functions** (sin, cos, sqrt, pow, random, etc.)
- **String manipulation** (split, replace, trim, upper/lower)
- **File operations** (read, write, append, delete)
- **Time and date** utilities
- **Array operations** (slice, filter, map)

## CLI
- **ollang script.oll** - Run a script including ollang
- **ollang -e "print('Hello')"** - Run code from the command line
- **ollang -repl** - Opens the REPL

## Notes:
- **Pretty sure repl is broken**
- **Thinking of porting this to a more reasonable language like rust, go, .net. Something easier to maintain this in**

## Syntax Examples

```c
# import
import example.oll
# Hello World
print("Hello, World!");
println("Hello, World!");

# Variables and functions
func greet(name) {
    return "Hello, " + name + "!";
}

println(greet("User"));

# Low-level memory operations
ptr = alloc(1024);          # Allocate 1KB
write(ptr, 0, 42, "i32");   # Write 32-bit integer
value = read(ptr, 0, "i32"); # Read it back
free(ptr);                   # Free memory

# System integration
import "user32.dll" as user32;
user32.MessageBoxA(0, "Hello", "Title", 0);

# Async operations
async func fetchData(url) {
    # Async operation
    return await http.get(url); # http.get isnt real, just an example your supposed to implement this yourself
}

# List comprehensions
numbers = [x for x in range(10) if x % 2 == 0];```

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void print_flag() {
    char *flag = getenv("FLAG");
    if (flag) {
        printf("\n🎉 Congratulations! Here's your flag:\n%s\n", flag);
    } else {
        printf("\n🎉 Flag: flag{buffer_overflow_pwned}\n");
    }
}

void vulnerable_function() {
    char buffer[64];
    printf("Enter your input: ");
    gets(buffer);  // 脆弱な関数：バッファオーバーフロー可能
    printf("You entered: %s\n", buffer);
}

int main() {
    printf("=== Buffer Overflow Challenge ===\n");
    printf("Can you get the flag?\n\n");

    vulnerable_function();

    printf("\nBye!\n");
    return 0;
}

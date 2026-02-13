#!/bin/bash

echo "=== Building CTF Challenge Docker Images ==="

# Web SQL Injection
echo ""
echo "[1/2] Building Web SQL Injection challenge..."
cd web-sqli
docker build -t ctf-web-sqli:latest .
if [ $? -eq 0 ]; then
    echo "✓ ctf-web-sqli:latest built successfully"
else
    echo "✗ Failed to build ctf-web-sqli"
fi
cd ..

# Pwn Buffer Overflow
echo ""
echo "[2/2] Building Pwn Buffer Overflow challenge..."
cd pwn-bufoverflow
docker build -t ctf-pwn-bufoverflow:latest .
if [ $? -eq 0 ]; then
    echo "✓ ctf-pwn-bufoverflow:latest built successfully"
else
    echo "✗ Failed to build ctf-pwn-bufoverflow"
fi
cd ..

echo ""
echo "=== Build Complete ==="
echo ""
echo "Available images:"
docker images | grep "^ctf-"

echo ""
echo "To test the images:"
echo "  Web:  docker run -d -p 8080:80 -e FLAG=\"flag{test}\" ctf-web-sqli:latest"
echo "  Pwn:  docker run -d -p 9999:9999 -e FLAG=\"flag{test}\" ctf-pwn-bufoverflow:latest"

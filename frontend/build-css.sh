#!/usr/bin/env bash

cat > static/input.css << EOF
@import "tailwindcss";
EOF

# Build Tailwind CSS with template scanning
tailwindcss -i static/input.css -o static/tailwind-only.css --content ./templates/**/*.html

# Combine Tailwind + daisyUI for production
cat static/{tailwind-only,daisyui-standalone}.css > static/style.css
rm -f static/{tailwind-only,input}.css

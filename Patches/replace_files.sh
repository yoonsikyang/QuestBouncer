#!/bin/bash

# Get the current directory
CURRENT_DIR="$(pwd)"
LIBRARY_DIR="$(pwd)/../Library"

# Check if Library directory exists
if [ ! -d "$LIBRARY_DIR" ]; then
    echo "Error: $LIBRARY_DIR directory not found"
    exit 1
fi

# Find all C# files in current directory
for cs_file in *.cs; do
    # Skip if no .cs files found
    if [ ! -f "$cs_file" ]; then
        echo "No C# files found in current directory"
        exit 0
    fi
    
    echo "Processing: $cs_file"
    
    # Find all matching files in Library and subdirectories
    while IFS= read -r target_file; do
        echo "  Replacing: $target_file"
        cp "$cs_file" "$target_file"
    done < <(find "$LIBRARY_DIR" -type f -name "$cs_file")
done

echo "Done!"

# Design: Show GGUF File Size in `veron ls`

## Purpose

Enhance the `veron ls` output with a SIZE column showing each model's GGUF file size, making it easy to see disk usage at a glance.

## Changes

### Modified Files

- **`Veron/Commands/CmdList.cs`** — Add SIZE column; resolve FROM path and stat the file

### Output Format

```
NAME          FROM                                  SIZE
------------------------------------------------------------------------
     minicpm   MiniCPM5-1B-Q4_K_M.gguf              0.9 GB
      qwopus   Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf    16.8 GB
qwopus-small  Qwopus3.6-27B-v2-MTP-Q4_K_M.gguf    missing

Total: 3 modelfile(s)
```

### Size Formatting

- `{value} GB` with one decimal place (e.g., `0.9 GB`, `16.8 GB`)
- Right-aligned in the SIZE column
- Conversion: `bytes / (1024.0 * 1024.0 * 1024.0)`

### Path Resolution

- Absolute `FROM` paths → used directly
- Relative `FROM` paths → joined with `--models-dir`

### Edge Cases

| Situation | Behavior |
|-----------|----------|
| File doesn't exist | Show `missing` |
| Zero-byte file | Show `0.0 GB` |
| Unreadable file (permissions) | Show `missing` |
| Symlink | Follow symlink (FileInfo.Length default) |

### Implementation Approach

After extracting the `FROM` target (already done in CmdList), resolve the full path and call `new FileInfo(fullPath).Length`. Wrap in try-catch for robustness. No new dependencies or model changes needed.

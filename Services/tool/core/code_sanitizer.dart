/// Final sanitization pass applied to all generated Dart code
/// before writing to disk. Catches residual corruption patterns.
class CodeSanitizer {
  static String sanitize(String code) {
    var result = code;

    // Fix backtick-n corruption → proper newline
    result = result.replaceAll('`n', '\n');

    // Remove stray backticks (from template literal leaks)
    result = result.replaceAll('`', '');

    // Fix corrupted 'nimport' → 'import' (from \n being eaten)
    result = result.replaceAll(RegExp(r'^nimport ', multiLine: true), 'import ');

    // Fix double semicolons
    result = result.replaceAll(';;', ';');

    // Fix empty lines with only whitespace
    result = result.replaceAll(RegExp(r'\n[ \t]+\n'), '\n\n');

    // Collapse 3+ blank lines into 2
    result = result.replaceAll(RegExp(r'\n{3,}'), '\n\n');

    return result;
  }
}

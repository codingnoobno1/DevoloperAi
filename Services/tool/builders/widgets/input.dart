class InputBuilder {
  static String build(Map<String, dynamic> config) {
    final label = config['label'] ?? '';
    final hint = config['hint'] ?? '';
    final obscure = config['obscureText'] ?? false;
    return "TextField(decoration: InputDecoration(labelText: '$label', hintText: '$hint'), obscureText: $obscure)";
  }
}

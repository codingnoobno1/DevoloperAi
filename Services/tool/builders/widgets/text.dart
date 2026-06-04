class TextBuilder {
  static String build(Map<String, dynamic> config) {
    final text = config['text'] ?? '';
    final style = config['style'] == 'heading' ? 'Theme.of(context).textTheme.headlineMedium' : 'null';
    return "Text('$text', style: $style)";
  }
}

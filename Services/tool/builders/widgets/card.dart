class CardBuilder {
  static String build(Map<String, dynamic> config) {
    final text = config['text'] ?? '';
    return "Card(child: Padding(padding: const EdgeInsets.all(16.0), child: Text('$text')))";
  }
}

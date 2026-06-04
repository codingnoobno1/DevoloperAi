class ButtonBuilder {
  static String build(Map<String, dynamic> config) {
    final text = config['text'] ?? '';
    return "ElevatedButton(onPressed: () {}, child: Text('$text'))";
  }
}

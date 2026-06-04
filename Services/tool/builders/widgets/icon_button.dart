class IconButtonBuilder {
  static String build(Map<String, dynamic> config) {
    final icon = config['icon'] ?? 'star';
    return "IconButton(onPressed: () {}, icon: Icon(Icons.$icon))";
  }
}

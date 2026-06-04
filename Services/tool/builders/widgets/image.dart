class ImageBuilder {
  static String build(Map<String, dynamic> config) {
    final url = config['url'] ?? 'https://via.placeholder.com/150';
    return "Image.network('$url')";
  }
}

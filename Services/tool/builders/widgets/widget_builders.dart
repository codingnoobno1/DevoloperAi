import '../utils/string_utils.dart';

class TextBuilder {
  static String build(Map<String, dynamic> config) {
    final text = config['text'] ?? '';
    final style = config['style'] == 'heading' ? 'Theme.of(context).textTheme.headlineMedium' : 'null';
    return "Text('\$text', style: \$style)";
  }
}

class ButtonBuilder {
  static String build(Map<String, dynamic> config) {
    final text = config['text'] ?? '';
    return "ElevatedButton(onPressed: () {}, child: Text('\$text'))";
  }
}

class CardBuilder {
  static String build(Map<String, dynamic> config) {
    final text = config['text'] ?? '';
    return "Card(child: Padding(padding: const EdgeInsets.all(16.0), child: Text('\$text')))";
  }
}

class TextFieldBuilder {
  static String build(Map<String, dynamic> config) {
    final label = config['label'] ?? '';
    final hint = config['hint'] ?? '';
    final obscure = config['obscureText'] ?? false;
    return "TextField(decoration: InputDecoration(labelText: '\$label', hintText: '\$hint'), obscureText: \$obscure)";
  }
}

class IconButtonBuilder {
  static String build(Map<String, dynamic> config) {
    final icon = config['icon'] ?? 'star';
    return "IconButton(onPressed: () {}, icon: Icon(Icons.\$icon))";
  }
}

class ImageBuilder {
  static String build(Map<String, dynamic> config) {
    final url = config['url'] ?? 'https://via.placeholder.com/150';
    return "Image.network('\$url')";
  }
}

class DividerBuilder {
  static String build() => "const Divider()";
}

class ProgressBuilder {
  static String build() => "const CircularProgressIndicator()";
}

import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class TextFieldNode extends WidgetNode {
  final String label;
  final bool obscureText;
  final String? controller;

  const TextFieldNode({required this.label, this.obscureText = false, this.controller});
}

class InputWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final label = config.properties['label'] ?? '';
    final obscure = config.properties['obscureText'] ?? false;
    
    return TextFieldNode(label: label, obscureText: obscure);
  }
}

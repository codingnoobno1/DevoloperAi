import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class TextWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final text = config.properties['text'] ?? '';
    final style = config.properties['style'];
    
    return TextNode(text, style: style);
  }
}

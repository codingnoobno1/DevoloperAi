import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class ListTileWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final title = config.properties['title'] ?? '';
    final subtitle = config.properties['subtitle'] ?? '';
    final leading = config.properties['leading'];
    
    return RawNode("""
ListTile(
  title: Text('$title'),
  subtitle: Text('$subtitle'),
  leading: ${leading != null ? "Icon(Icons.$leading)" : "null"},
)""");
  }
}

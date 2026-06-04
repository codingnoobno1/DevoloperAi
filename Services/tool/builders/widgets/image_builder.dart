import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';

class ImageWidgetBuilder implements WidgetBuilder {
  @override
  WidgetNode build(WidgetModel config, AppModel model) {
    final url = config.properties['url'] ?? 'https://via.placeholder.com/150';
    return RawNode("Image.network('$url')");
  }
}

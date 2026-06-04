import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class MapVariant2Builder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return 'Column(children: [' + children.join(', ') + '])';
  }
}

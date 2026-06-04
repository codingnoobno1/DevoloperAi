import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class UtilityVariant7Builder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return 'Column(children: [' + children.join(', ') + '])';
  }
}

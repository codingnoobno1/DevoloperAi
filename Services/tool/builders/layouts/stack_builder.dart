import '../../core/interfaces.dart';
import '../../core/models.dart';

class StackLayoutBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    final childrenList = children.join(', ');
    return "Stack(children: [$childrenList])";
  }
}

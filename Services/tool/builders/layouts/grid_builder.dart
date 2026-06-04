import '../../core/interfaces.dart';
import '../../core/models.dart';

class GridLayoutBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    final childrenList = children.join(', ');
    return "GridView.count(crossAxisCount: 2, children: [$childrenList])";
  }
}

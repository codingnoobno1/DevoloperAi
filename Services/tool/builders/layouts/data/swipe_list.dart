import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class SwipeActionsListBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
ListView.builder(
  itemBuilder: (context, index) => Dismissible(
    key: Key('\$index'),
    background: Container(color: Colors.red, child: const Icon(Icons.delete)),
    child: ListTile(title: Text("Swipable Item \$index")),
  ),
)
""";
  }
}

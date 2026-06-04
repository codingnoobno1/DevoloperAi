import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class StaggeredGridBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    // Note: The generated code will need 'package:flutter_staggered_grid_view/flutter_staggered_grid_view.dart'
    return """
MasonryGridView.count(
  crossAxisCount: 2,
  mainAxisSpacing: 4,
  crossAxisSpacing: 4,
  itemBuilder: (context, index) => Card(child: Padding(padding: const EdgeInsets.all(8), child: Text("Item \$index"))),
)
""";
  }
}

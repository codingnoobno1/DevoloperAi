import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class ProductGridBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
GridView.builder(
  gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, childAspectRatio: 0.7),
  itemBuilder: (context, index) => Card(child: Column(children: [Image.network("https://via.placeholder.com/150"), const Text("Product Name"), const Text("\\\$99.99")])),
)
""";
  }
}

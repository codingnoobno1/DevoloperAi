import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class CartScreenBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Column(
  children: [
    Expanded(child: ListView.builder(itemBuilder: (context, index) => ListTile(leading: Image.network("https://via.placeholder.com/50"), title: const Text("Product"), subtitle: const Text("\\\$99.99")))),
    Padding(padding: const EdgeInsets.all(16), child: ElevatedButton(onPressed: () {}, child: const Text("Checkout"))),
  ],
)
""";
  }
}

import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class LoginSplitBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    final childrenList = children.join(', ');
    return """
Row(
  children: [
    Expanded(child: Container(color: Colors.blue, child: const Center(child: Icon(Icons.lock, size: 100, color: Colors.white)))),
    Expanded(child: Padding(padding: const EdgeInsets.all(32), child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [$childrenList]))),
  ],
)
""";
  }
}

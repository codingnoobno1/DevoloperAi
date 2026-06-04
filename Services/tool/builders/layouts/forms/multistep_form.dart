import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class MultiStepFormBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Column(
  children: [
    const LinearProgressIndicator(value: 0.3),
    Expanded(child: ListView(padding: const EdgeInsets.all(16), children: [${children.join(', ')}])),
    Padding(padding: const EdgeInsets.all(16), child: ElevatedButton(onPressed: () {}, child: const Text("Next"))),
  ],
)
""";
  }
}

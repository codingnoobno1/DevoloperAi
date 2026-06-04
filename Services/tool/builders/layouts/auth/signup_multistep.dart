import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class SignupMultiStepBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Column(
  children: [
    const LinearProgressIndicator(value: 0.5),
    Expanded(child: PageView(children: [${children.join(', ')}])),
  ],
)
""";
  }
}

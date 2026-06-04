import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class CheckoutStepsBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    return """
Column(
  children: [
    const Stepper(steps: [Step(title: Text("Address"), content: Text("...")), Step(title: Text("Payment"), content: Text("..."))]),
    ${children.join(', ')},
  ],
)
""";
  }
}

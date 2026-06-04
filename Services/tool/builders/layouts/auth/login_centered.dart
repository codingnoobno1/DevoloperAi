import '../../../core/interfaces.dart';
import '../../../core/models.dart';

class LoginCenteredBuilder implements LayoutBuilder {
  @override
  String build(LayoutModel config, List<String> children, AppModel model) {
    final childrenList = children.join(', ');
    return """
Center(
  child: SingleChildScrollView(
    padding: const EdgeInsets.all(24),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [$childrenList],
    ),
  ),
)
""";
  }
}

import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class HomeLayoutBuilder implements LayoutBuilder {
  @override
  WidgetNode build(LayoutModel config, List<WidgetNode> children, AppModel model) {
    return ScaffoldNode(
      appBar: AppBarNode(
        title: "Home",
        centerTitle: false,
        actions: const [
          IconNode("Icons.notifications_outlined"),
          SizedBoxNode(width: 16),
        ],
      ),
      body: SingleChildScrollViewNode(
        child: ColumnNode(
          crossAxisAlignment: "CrossAxisAlignment.start",
          children: [
            PaddingNode(
              padding: const EdgeInsetsExpr.all(16.0),
              child: TextNode(
                "Welcome Back!",
                style: TextStyleExpr(
                  fontSize: 24, 
                  fontWeight: "bold",
                  color: ColorExpr.material("black"),
                ),
              ),
            ),
            ...children,
          ],
        ),
      ),
    );
  }
}

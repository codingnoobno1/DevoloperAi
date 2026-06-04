import '../../core/ast.dart';
import '../../core/interfaces.dart';
import '../../core/models.dart';
import '../../core/expressions.dart';

class AuthLoginTemplate implements TemplateBuilder {
  @override
  WidgetNode build(Map<String, dynamic> props, List<WidgetNode> children, AppModel model) {
    final title = props['title'] ?? 'Welcome Back';
    final subtitle = props['subtitle'] ?? 'Sign in to continue';

    return ScaffoldNode(
      body: CenterNode(
        child: ColumnNode(
          mainAxisAlignment: "MainAxisAlignment.center",
          children: [
            // Header
            SectionNode(
              name: "header",
              child: ColumnNode(
                crossAxisAlignment: "CrossAxisAlignment.center",
                children: [
                  const IconNode("Icons.bolt", size: 64, color: ColorExpr.material("teal")),
                  const SizedBoxNode(height: 24), 
                  TextNode(title, style: const TextStyleExpr(fontSize: 32, fontWeight: "bold", letterSpacing: -1)),
                  const SizedBoxNode(height: 8),
                  TextNode(subtitle, style: TextStyleExpr(fontSize: 16, color: ColorExpr.material("grey"))),
                ],
              ),
            ),
            
            const SizedBoxNode(height: 32), 
            
            // Body (The Form)
            SectionNode(
              name: "body",
              child: CardNode(
                child: PaddingNode(
                  padding: const EdgeInsetsExpr.token("lg"),
                  child: ColumnNode(
                    spacing: 16.0,
                    children: children,
                  ),
                ),
              ),
            ),

            const SizedBoxNode(height: 24),

            // Footer
            SectionNode(
              name: "footer",
              child: const TextButtonNode(
                text: "Don't have an account? Sign Up",
                action: "() {}",
                style: TextStyleExpr(fontWeight: "bold", color: ColorExpr.material("teal")),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

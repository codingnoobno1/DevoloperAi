/// Value expressions for the AST.
/// These move the AST away from Dart strings and toward a framework-agnostic structure.
abstract class Expr {
  const Expr();
}

class EdgeInsetsExpr extends Expr {
  final String kind; // 'all', 'symmetric', 'only', 'token'
  final double? value;
  final double? horizontal;
  final double? vertical;
  final double? top;
  final double? bottom;
  final double? left;
  final double? right;
  final String? token; // e.g. 'paddingPage'

  const EdgeInsetsExpr.all(this.value)
      : kind = 'all', horizontal = null, vertical = null, top = null, bottom = null, left = null, right = null, token = null;

  const EdgeInsetsExpr.symmetric({this.horizontal, this.vertical})
      : kind = 'symmetric', value = null, top = null, bottom = null, left = null, right = null, token = null;

  const EdgeInsetsExpr.only({this.top, this.bottom, this.left, this.right})
      : kind = 'only', value = null, horizontal = null, vertical = null, token = null;

  const EdgeInsetsExpr.token(this.token)
      : kind = 'token', value = null, horizontal = null, vertical = null, top = null, bottom = null, left = null, right = null;

  const EdgeInsetsExpr.zero()
      : kind = 'all', value = 0, horizontal = null, vertical = null, top = null, bottom = null, left = null, right = null, token = null;
}

class AlignmentExpr extends Expr {
  final String value; // 'center', 'topLeft', 'topRight', 'bottomLeft', 'bottomRight', 'centerLeft', 'centerRight', 'topCenter', 'bottomCenter'
  const AlignmentExpr(this.value);
  
  static const center = AlignmentExpr('center');
  static const centerLeft = AlignmentExpr('centerLeft');
  static const centerRight = AlignmentExpr('centerRight');
}

class ColorExpr extends Expr {
  final String kind; // 'material', 'hex', 'design'
  final String value;
  const ColorExpr.material(this.value) : kind = 'material';
  const ColorExpr.hex(this.value) : kind = 'hex';
  const ColorExpr.design(this.value) : kind = 'design';
}

class TextStyleExpr extends Expr {
  final String? themeStyle; // e.g. 'titleLarge', 'bodyMedium'
  final double? fontSize;
  final String? fontWeight;
  final ColorExpr? color;
  final double? letterSpacing;

  const TextStyleExpr({
    this.themeStyle,
    this.fontSize,
    this.fontWeight,
    this.color,
    this.letterSpacing,
  });
}

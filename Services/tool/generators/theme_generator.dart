import '../core/file_writer.dart';
import '../core/models.dart';
import 'base_generator.dart';

class ThemeGenerator implements BaseGenerator {
  @override
  Future<void> generate(AppModel model) async {
    _generateDesignTokens(model);
    _generateTheme(model);
  }

  void _generateTheme(AppModel model) {
    final light = Map<String, dynamic>.from(model.theme['themes']?['light']?['colors'] ?? {});
    final dark = Map<String, dynamic>.from(model.theme['themes']?['dark']?['colors'] ?? {});
    final textStyles = Map<String, dynamic>.from(model.theme['themes']?['light']?['textStyles'] ?? {});
    final components = Map<String, dynamic>.from(model.theme['components'] ?? {});

    final textThemeCode = _generateTextTheme(textStyles);

    final code = """
import 'dart:ui';
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'design_tokens.dart';

class AppTheme {
  static Color _hex(String hex) {
    hex = hex.replaceAll('#', '');
    if (hex.length == 3) {
      hex = hex.split('').map((c) => '\$c\$c').join();
    }
    if (hex.length == 6) {
      hex = 'FF\$hex';
    }
    return Color(int.parse(hex, radix: 16));
  }

  static ThemeData get lightTheme {
    return ThemeData(
      useMaterial3: true,
      colorScheme: ColorScheme(
        brightness: Brightness.light,
        primary: _hex("${light['primary'] ?? "#009688"}"),
        onPrimary: _hex("${light['onPrimary'] ?? "#FFFFFF"}"),
        primaryContainer: _hex("${light['primaryContainer'] ?? "#B2DFDB"}"),
        onPrimaryContainer: _hex("${light['onPrimaryContainer'] ?? "#004D40"}"),
        secondary: _hex("${light['secondary'] ?? "#00796B"}"),
        onSecondary: _hex("${light['onSecondary'] ?? "#FFFFFF"}"),
        surface: _hex("${light['surface'] ?? "#E0F2F1"}"),
        onSurface: _hex("${light['onSurface'] ?? "#00201C"}"),
        surfaceVariant: _hex("${light['divider'] ?? "#CDE7E3"}"),
        outline: _hex("${light['divider'] ?? "#CDE7E3"}"),
        error: _hex("${light['error'] ?? "#BA1A1A"}"),
        onError: _hex("${light['onError'] ?? "#FFFFFF"}"),
      ),
      scaffoldBackgroundColor: _hex("${light['background'] ?? "#F4FDFC"}"),
      cardTheme: CardThemeData(
        color: _hex("${light['card'] ?? "#E0F2F1"}"),
        elevation: ${components['Card']?['elevation'] ?? 2.0},
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(AppDesign.radiusLg),
        ),
      ),
      textTheme: $textThemeCode,
      appBarTheme: const AppBarTheme(
        centerTitle: false,
        elevation: 0,
        backgroundColor: Colors.transparent,
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: _hex("${light['primary'] ?? "#009688"}"),
          foregroundColor: _hex("${light['onPrimary'] ?? "#FFFFFF"}"),
          padding: AppDesign.paddingButton,
          minimumSize: const Size(0, ${components['Button']?['height'] ?? 48.0}),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(AppDesign.radiusMd),
          ),
        ),
      ),
      extensions: [
        AppSpacingExtension(
          page: AppDesign.spacingPage,
          card: AppDesign.spacingMd,
        ),
      ],
    );
  }

  static ThemeData get darkTheme {
    return ThemeData(
      useMaterial3: true,
      colorScheme: ColorScheme(
        brightness: Brightness.dark,
        primary: _hex("${dark['primary'] ?? "#26A69A"}"),
        onPrimary: _hex("${dark['onPrimary'] ?? "#003732"}"),
        primaryContainer: _hex("${dark['primaryContainer'] ?? "#005047"}"),
        onPrimaryContainer: _hex("${dark['onPrimaryContainer'] ?? "#B2DFDB"}"),
        secondary: _hex("${dark['secondary'] ?? "#80CBC4"}"),
        onSecondary: _hex("${dark['onSecondary'] ?? "#003732"}"),
        surface: _hex("${dark['surface'] ?? "#1B2F2C"}"),
        onSurface: _hex("${dark['onSurface'] ?? "#E0F2F1"}"),
        surfaceVariant: _hex("${dark['divider'] ?? "#2E4F4A"}"),
        outline: _hex("${dark['divider'] ?? "#2E4F4A"}"),
        error: _hex("${dark['error'] ?? "#FFB4AB"}"),
        onError: _hex("${dark['onError'] ?? "#690005"}"),
      ),
      scaffoldBackgroundColor: _hex("${dark['background'] ?? "#0F1F1D"}"),
      cardTheme: CardThemeData(
        color: _hex("${dark['card'] ?? "#1B2F2C"}"),
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(AppDesign.radiusLg),
        ),
      ),
      textTheme: GoogleFonts.interTextTheme(ThemeData.dark().textTheme),
      extensions: [
        AppSpacingExtension(
          page: AppDesign.spacingPage,
          card: AppDesign.spacingMd,
        ),
      ],
    );
  }
}

class AppSpacingExtension extends ThemeExtension<AppSpacingExtension> {
  final double page;
  final double card;

  const AppSpacingExtension({required this.page, required this.card});

  @override
  AppSpacingExtension copyWith({double? page, double? card}) {
    return AppSpacingExtension(
      page: page ?? this.page,
      card: card ?? this.card,
    );
  }

  @override
  AppSpacingExtension lerp(ThemeExtension<AppSpacingExtension>? other, double t) {
    if (other is! AppSpacingExtension) return this;
    return AppSpacingExtension(
      page: lerpDouble(page, other.page, t)!,
      card: lerpDouble(card, other.card, t)!,
    );
  }
}
""";
    FileWriter.write('lib_gen/core/theme/app_theme.dart', code);
  }

  String _generateTextTheme(Map<String, dynamic> styles) {
    const textStyleMap = {
      'heading': 'headlineLarge',
      'title': 'titleLarge',
      'body': 'bodyMedium',
      'caption': 'labelSmall',
    };

    final buffer = StringBuffer("TextTheme(\n");
    styles.forEach((key, style) {
      final flutterKey = textStyleMap[key] ?? key;
      final fontSize = style['fontSize'] ?? 14;
      final fontWeight = _mapFontWeight(style['fontWeight']);
      final letterSpacing = style['letterSpacing'] ?? 0.0;
      final color = style['color'];
      
      buffer.writeln("        $flutterKey: GoogleFonts.inter(");
      buffer.writeln("          fontSize: $fontSize.toDouble(),");
      buffer.writeln("          fontWeight: $fontWeight,");
      buffer.writeln("          letterSpacing: $letterSpacing.toDouble(),");
      if (color != null) {
        buffer.writeln("          color: _hex('$color'),");
      }
      buffer.writeln("        ),");
    });
    buffer.write("      )");
    return buffer.toString();
  }

  String _mapFontWeight(String? weight) {
    switch (weight) {
      case 'bold': return 'FontWeight.bold';
      case 'w900': return 'FontWeight.w900';
      case 'w800': return 'FontWeight.w800';
      case 'w700': return 'FontWeight.w700';
      case 'w600': return 'FontWeight.w600';
      case 'w500': return 'FontWeight.w500';
      case 'w400': return 'FontWeight.w400';
      case 'w300': return 'FontWeight.w300';
      case 'light': return 'FontWeight.light';
      default: return 'FontWeight.normal';
    }
  }

  void _generateDesignTokens(AppModel model) {
    final tokens = Map<String, dynamic>.from(model.theme['tokens'] ?? {});
    final spacing = Map<String, dynamic>.from(tokens['spacing'] ?? {});
    final radius = Map<String, dynamic>.from(tokens['radius'] ?? {});

    final code = """
import 'package:flutter/material.dart';

class AppDesign {
  static const double spacingXs = ${spacing['xs'] ?? 4.0};
  static const double spacingSm = ${spacing['sm'] ?? 8.0};
  static const double spacingMd = ${spacing['md'] ?? 16.0};
  static const double spacingLg = ${spacing['lg'] ?? 24.0};
  static const double spacingXl = ${spacing['xl'] ?? 32.0};
  static const double spacingPage = ${spacing['page'] ?? 16.0};

  static const double radiusSm = ${radius['sm'] ?? 8.0};
  static const double radiusMd = ${radius['md'] ?? 12.0};
  static const double radiusLg = ${radius['lg'] ?? 20.0};
  static const double radiusFull = ${radius['full'] ?? 99.0};

  static const EdgeInsets paddingPage = EdgeInsets.all(spacingPage);
  static const EdgeInsets paddingCard = EdgeInsets.all(spacingMd);
  static const EdgeInsets paddingLg = EdgeInsets.all(spacingLg);
  static const EdgeInsets paddingMd = EdgeInsets.all(spacingMd);
  static const EdgeInsets paddingSm = EdgeInsets.all(spacingSm);
  static const EdgeInsets paddingButton = EdgeInsets.symmetric(vertical: spacingMd, horizontal: spacingLg);
  
  static Widget verticalSpace(double value) => SizedBox(height: value);
  static Widget horizontalSpace(double value) => SizedBox(width: value);
  
  static final BorderRadius borderRadiusMd = BorderRadius.circular(radiusMd);
  static final BorderRadius borderRadiusLg = BorderRadius.circular(radiusLg);
  static final BorderRadius borderRadiusXl = BorderRadius.circular(spacingXl);
}
""";
    FileWriter.write('lib_gen/core/theme/design_tokens.dart', code);
  }
}

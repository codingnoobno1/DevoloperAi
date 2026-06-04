import '../core/file_writer.dart';
import '../core/models.dart';
import 'base_generator.dart';

class UIGenerator implements BaseGenerator {
  @override
  Future<void> generate(AppModel model) async {
    _generateAdapterInterface();
    _generateMaterialAdapter();
    _generateGlassAdapter();
    _generateNeumorphicAdapter();
    _generateUIFactory(model);
  }

  void _generateAdapterInterface() {
    final code = """
import 'package:flutter/material.dart';

abstract class UIAdapter {
  Widget card({required Widget child, Color? color, double? elevation});
  Widget button({required VoidCallback onPressed, required Widget child, Color? color});
  Widget textField({required String label, TextEditingController? controller, bool obscureText = false});
  Widget navBar({required int currentIndex, required List<NavItem> items, required Function(int) onTap});
}

class NavItem {
  final String label;
  final IconData icon;
  final Color? color;
  NavItem({required this.label, required this.icon, this.color});
}
""";
    FileWriter.write('lib_gen/core/ui/ui_adapter.dart', code);
  }

  void _generateMaterialAdapter() {
    final code = """
import 'package:flutter/material.dart';
import '../theme/design_tokens.dart';
import 'ui_adapter.dart';

class MaterialAdapter implements UIAdapter {
  @override
  Widget card({required Widget child, Color? color, double? elevation}) {
    return Card(
      color: color,
      elevation: elevation ?? 0.0,
      shape: RoundedRectangleBorder(
        borderRadius: AppDesign.borderRadiusLg,
        side: BorderSide(color: Colors.grey.withOpacity(0.1)),
      ),
      child: child,
    );
  }

  @override
  Widget button({required VoidCallback onPressed, required Widget child, Color? color}) {
    return ElevatedButton(
      onPressed: onPressed,
      style: ElevatedButton.styleFrom(
        backgroundColor: color,
        foregroundColor: Colors.white,
        elevation: 0,
        shape: RoundedRectangleBorder(borderRadius: AppDesign.borderRadiusMd),
        padding: AppDesign.paddingButton,
      ),
      child: child,
    );
  }

  @override
  Widget textField({required String label, TextEditingController? controller, bool obscureText = false}) {
    return TextField(
      controller: controller,
      obscureText: obscureText,
      decoration: InputDecoration(
        labelText: label,
        border: OutlineInputBorder(borderRadius: AppDesign.borderRadiusMd),
        enabledBorder: OutlineInputBorder(
          borderRadius: AppDesign.borderRadiusMd,
          borderSide: BorderSide(color: Colors.grey.withOpacity(0.2)),
        ),
        filled: true,
        fillColor: Colors.grey.withOpacity(0.05),
        contentPadding: AppDesign.paddingPage,
      ),
    );
  }

  @override
  Widget navBar({required int currentIndex, required List<NavItem> items, required Function(int) onTap}) {
    return NavigationBar(
      selectedIndex: currentIndex,
      onDestinationSelected: onTap,
      elevation: 0,
      backgroundColor: Colors.white,
      indicatorColor: Colors.teal.withOpacity(0.1),
      destinations: items.map((item) => NavigationDestination(
        icon: Icon(item.icon, color: Colors.teal),
        label: item.label,
      )).toList(),
    );
  }
}
""";
    FileWriter.write('lib_gen/core/ui/material_adapter.dart', code);
  }

  void _generateGlassAdapter() {
    final code = """
import 'dart:ui';
import 'package:flutter/material.dart';
import '../theme/design_tokens.dart';
import 'ui_adapter.dart';

class GlassAdapter implements UIAdapter {
  @override
  Widget card({required Widget child, Color? color, double? elevation}) {
    return ClipRRect(
      borderRadius: AppDesign.borderRadiusLg,
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 12, sigmaY: 12),
        child: Container(
          decoration: BoxDecoration(
            color: (color ?? Colors.white).withOpacity(0.15),
            borderRadius: AppDesign.borderRadiusLg,
            border: Border.all(color: Colors.white.withOpacity(0.2)),
          ),
          child: child,
        ),
      ),
    );
  }

  @override
  Widget button({required VoidCallback onPressed, required Widget child, Color? color}) {
    return GestureDetector(
      onTap: onPressed,
      child: ClipRRect(
        borderRadius: AppDesign.borderRadiusMd,
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
          child: Container(
            padding: AppDesign.paddingButton,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  (color ?? Colors.teal).withOpacity(0.4),
                  (color ?? Colors.teal).withOpacity(0.2),
                ],
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
              ),
              borderRadius: AppDesign.borderRadiusMd,
              border: Border.all(color: Colors.white.withOpacity(0.3)),
            ),
            child: Center(child: child),
          ),
        ),
      ),
    );
  }

  @override
  Widget textField({required String label, TextEditingController? controller, bool obscureText = false}) {
    return ClipRRect(
      borderRadius: AppDesign.borderRadiusMd,
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 8, sigmaY: 8),
        child: Container(
          decoration: BoxDecoration(
            color: Colors.white.withOpacity(0.08),
            borderRadius: AppDesign.borderRadiusMd,
            border: Border.all(color: Colors.white.withOpacity(0.15)),
          ),
          child: TextField(
            controller: controller,
            obscureText: obscureText,
            style: const TextStyle(color: Colors.white),
            decoration: InputDecoration(
              labelText: label,
              labelStyle: const TextStyle(color: Colors.white70),
              border: InputBorder.none,
              contentPadding: AppDesign.paddingPage,
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget navBar({required int currentIndex, required List<NavItem> items, required Function(int) onTap}) {
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: AppDesign.spacingMd, vertical: AppDesign.spacingLg),
      child: ClipRRect(
        borderRadius: AppDesign.borderRadiusXl,
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 25, sigmaY: 25),
          child: Container(
            height: 72,
            decoration: BoxDecoration(
              color: Colors.black.withOpacity(0.3),
              borderRadius: AppDesign.borderRadiusXl,
              border: Border.all(color: Colors.white.withOpacity(0.1)),
            ),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceAround,
              children: items.asMap().entries.map((entry) {
                final i = entry.key;
                final item = entry.value;
                final isSelected = currentIndex == i;
                return GestureDetector(
                  onTap: () => onTap(i),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        item.icon,
                        color: isSelected ? (item.color ?? Colors.tealAccent) : Colors.white60,
                        size: 24,
                      ),
                      const SizedBox(height: 4),
                      if (isSelected)
                        Container(
                          width: 4,
                          height: 4,
                          decoration: BoxDecoration(
                            color: item.color ?? Colors.tealAccent,
                            shape: BoxShape.circle,
                          ),
                        ),
                    ],
                  ),
                );
              }).toList(),
            ),
          ),
        ),
      ),
    );
  }
}
""";
    FileWriter.write('lib_gen/core/ui/glass_adapter.dart', code);
  }

  void _generateNeumorphicAdapter() {
    final code = """
import 'package:flutter/material.dart';
import 'package:flutter_neumorphic_plus/flutter_neumorphic.dart';
import '../theme/design_tokens.dart';
import 'ui_adapter.dart';

class NeumorphicAdapter implements UIAdapter {
  @override
  Widget card({required Widget child, Color? color, double? elevation}) {
    return Neumorphic(
      style: NeumorphicStyle(
        depth: elevation ?? 6,
        intensity: 0.8,
        color: color,
        boxShape: NeumorphicBoxShape.roundRect(AppDesign.borderRadiusLg),
      ),
      child: child,
    );
  }

  @override
  Widget button({required VoidCallback onPressed, required Widget child, Color? color}) {
    return NeumorphicButton(
      onPressed: onPressed,
      style: NeumorphicStyle(
        color: color,
        depth: 4,
        intensity: 0.8,
        shape: NeumorphicShape.flat,
        boxShape: NeumorphicBoxShape.roundRect(AppDesign.borderRadiusMd),
      ),
      padding: AppDesign.paddingButton,
      child: Center(child: child),
    );
  }

  @override
  Widget textField({required String label, TextEditingController? controller, bool obscureText = false}) {
    return Neumorphic(
      style: NeumorphicStyle(
        depth: -6,
        intensity: 0.8,
        boxShape: NeumorphicBoxShape.roundRect(AppDesign.borderRadiusMd),
      ),
      padding: const EdgeInsets.symmetric(horizontal: AppDesign.spacingMd),
      child: TextField(
        controller: controller,
        obscureText: obscureText,
        decoration: InputDecoration(
          labelText: label,
          border: InputBorder.none,
          contentPadding: const EdgeInsets.symmetric(vertical: AppDesign.spacingMd),
        ),
      ),
    );
  }

  @override
  Widget navBar({required int currentIndex, required List<NavItem> items, required Function(int) onTap}) {
    return Container(
      height: 88,
      padding: const EdgeInsets.symmetric(vertical: AppDesign.spacingSm),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceAround,
        children: items.asMap().entries.map((entry) {
          final i = entry.key;
          final item = entry.value;
          final isSelected = currentIndex == i;
          return NeumorphicButton(
            onPressed: () => onTap(i),
            style: NeumorphicStyle(
              depth: isSelected ? -4 : 4,
              intensity: 0.8,
              shape: isSelected ? NeumorphicShape.convex : NeumorphicShape.flat,
              boxShape: const NeumorphicBoxShape.circle(),
            ),
            child: Icon(item.icon, color: isSelected ? (item.color ?? Colors.teal) : Colors.grey, size: 28),
          );
        }).toList(),
      ),
    );
  }
}
""";
    FileWriter.write('lib_gen/core/ui/neumorphic_adapter.dart', code);
  }

  void _generateUIFactory(AppModel model) {
    final style = model.theme['style'] ?? 'material';
    final code = """
import 'package:flutter/material.dart';
import 'ui_adapter.dart';
import 'material_adapter.dart';
import 'glass_adapter.dart';
import 'neumorphic_adapter.dart';

class UIFactory {
  static String mode = "$style";
  static late UIAdapter _adapter;

  static void init(String mode) {
    UIFactory.mode = mode;
    switch (mode) {
      case 'glass':
        _adapter = GlassAdapter();
        break;
      case 'neumorphic':
        _adapter = NeumorphicAdapter();
        break;
      default:
        _adapter = MaterialAdapter();
    }
  }

  static UIAdapter get adapter {
    // Auto-init if not done
    try {
      return _adapter;
    } catch (_) {
      init(mode);
      return _adapter;
    }
  }

  static Widget card({required Widget child, Color? color, double? elevation}) => 
      adapter.card(child: child, color: color, elevation: elevation);

  static Widget button({required VoidCallback onPressed, required Widget child, Color? color}) => 
      adapter.button(onPressed: onPressed, child: child, color: color);

  static Widget textField({required String label, TextEditingController? controller, bool obscureText = false}) => 
      adapter.textField(label: label, controller: controller, obscureText: obscureText);

  static Widget navBar({required int currentIndex, required List<NavItem> items, required Function(int) onTap}) => 
      adapter.navBar(currentIndex: currentIndex, items: items, onTap: onTap);
}
""";
    FileWriter.write('lib_gen/core/ui/ui_factory.dart', code);
  }
}

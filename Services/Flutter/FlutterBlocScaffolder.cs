using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;

namespace Syncro.Desktop.Services.Flutter
{
    /// <summary>
    /// Imposes a BLoC (flutter_bloc + Cubit + Repository) architecture onto a freshly-created
    /// Flutter project, replacing the stock counter app. See flutter-bloc-aimapper.md (FB0).
    ///
    /// Layout produced (mirrors the "thunder" generator + the BLoC↔MAUI map):
    ///   lib/
    ///     main.dart                                  MultiRepositoryProvider + MultiBlocProvider
    ///     core/theme/app_theme.dart
    ///     core/routes/app_routes.dart
    ///     features/home/bloc/home_cubit.dart         (state holder ≈ MAUI ViewModel)
    ///     features/home/bloc/home_state.dart
    ///     features/home/repository/home_repository.dart   (data ≈ MAUI Service/Repository)
    ///     features/home/ui/home_page.dart            (view ≈ .razor / ContentPage)
    /// </summary>
    public class FlutterBlocScaffolder
    {
        /// <summary>Add flutter_bloc deps + write the BLoC skeleton into an existing project root.</summary>
        public async Task<bool> ScaffoldBlocAsync(
            string projectRoot, string appName, Action<string>? onLog = null, CancellationToken ct = default)
        {
            try
            {
                if (!Directory.Exists(projectRoot))
                {
                    onLog?.Invoke($"[BLoC] Project root not found: {projectRoot}");
                    return false;
                }

                onLog?.Invoke("[BLoC] Adding flutter_bloc + equatable to pubspec…");
                await RunAsync("flutter", "pub add flutter_bloc equatable", projectRoot, onLog, ct);

                onLog?.Invoke("[BLoC] Writing BLoC architecture into lib/…");
                foreach (var (relPath, content) in BuildFiles(appName))
                {
                    var full = Path.Combine(projectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await File.WriteAllTextAsync(full, content, ct);
                    onLog?.Invoke($"[BLoC]   + {relPath}");
                }

                onLog?.Invoke("[BLoC] BLoC architecture ready (features/home: cubit · repository · ui).");
                return true;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"[BLoC ERROR] {ex.Message}");
                return false;
            }
        }

        private static async Task RunAsync(string cmd, string args, string cwd, Action<string>? onLog, CancellationToken ct)
        {
            var cli = Cli.Wrap(cmd).WithArguments(args).WithWorkingDirectory(cwd)
                         .WithValidation(CommandResultValidation.None);
            await foreach (var ev in cli.ListenAsync(ct))
            {
                switch (ev)
                {
                    case StandardOutputCommandEvent o when !string.IsNullOrWhiteSpace(o.Text): onLog?.Invoke($"[pub] {o.Text}"); break;
                    case StandardErrorCommandEvent e when !string.IsNullOrWhiteSpace(e.Text): onLog?.Invoke($"[pub] {e.Text}"); break;
                }
            }
        }

        private static IEnumerable<(string, string)> BuildFiles(string appName)
        {
            var title = string.IsNullOrWhiteSpace(appName) ? "Syncro App" : appName;

            yield return ("lib/main.dart", $$"""
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'core/theme/app_theme.dart';
import 'features/home/bloc/home_cubit.dart';
import 'features/home/repository/home_repository.dart';
import 'features/home/ui/home_page.dart';

void main() => runApp(const SyncroApp());

class SyncroApp extends StatelessWidget {
  const SyncroApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MultiRepositoryProvider(
      providers: [
        RepositoryProvider<HomeRepository>(create: (_) => HomeRepository()),
      ],
      child: MultiBlocProvider(
        providers: [
          BlocProvider<HomeCubit>(
            create: (ctx) => HomeCubit(ctx.read<HomeRepository>())..load(),
          ),
        ],
        child: MaterialApp(
          title: '{{title}}',
          debugShowCheckedModeBanner: false,
          theme: AppTheme.dark,
          home: const HomePage(),
        ),
      ),
    );
  }
}
""");

            yield return ("lib/core/theme/app_theme.dart", """
import 'package:flutter/material.dart';

class AppTheme {
  static ThemeData get dark => ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF5B6CFF),
          brightness: Brightness.dark,
        ),
      );
}
""");

            yield return ("lib/core/routes/app_routes.dart", """
import 'package:flutter/material.dart';
import '../../features/home/ui/home_page.dart';

class AppRoutes {
  static const String home = '/';

  static Map<String, WidgetBuilder> get routes => {
        home: (_) => const HomePage(),
      };
}
""");

            yield return ("lib/features/home/bloc/home_state.dart", """
import 'package:equatable/equatable.dart';

enum HomeStatus { initial, loading, loaded, error }

class HomeState extends Equatable {
  final HomeStatus status;
  final List<String> items;
  final String? message;

  const HomeState({required this.status, this.items = const [], this.message});

  const HomeState.initial() : this(status: HomeStatus.initial);
  const HomeState.loading() : this(status: HomeStatus.loading);
  const HomeState.loaded(List<String> items) : this(status: HomeStatus.loaded, items: items);
  const HomeState.error(String message) : this(status: HomeStatus.error, message: message);

  @override
  List<Object?> get props => [status, items, message];
}
""");

            yield return ("lib/features/home/bloc/home_cubit.dart", """
import 'package:flutter_bloc/flutter_bloc.dart';
import '../repository/home_repository.dart';
import 'home_state.dart';

/// State holder for the Home feature (≈ a MAUI ViewModel). UI dispatches intents here;
/// it talks to the repository, never the other way round.
class HomeCubit extends Cubit<HomeState> {
  final HomeRepository _repository;
  HomeCubit(this._repository) : super(const HomeState.initial());

  Future<void> load() async {
    emit(const HomeState.loading());
    try {
      final items = await _repository.fetchItems();
      emit(HomeState.loaded(items));
    } catch (e) {
      emit(HomeState.error(e.toString()));
    }
  }
}
""");

            yield return ("lib/features/home/repository/home_repository.dart", """
/// Data layer for the Home feature (≈ a MAUI Service/Repository). Swap the stub for an
/// ApiService/DbService call — the cubit doesn't change.
class HomeRepository {
  Future<List<String>> fetchItems() async {
    await Future.delayed(const Duration(milliseconds: 300));
    return const [
      'Welcome to Syncro',
      'BLoC architecture is wired (Cubit · Repository · UI)',
      'Edit features/home to start building',
    ];
  }
}
""");

            yield return ("lib/features/home/ui/home_page.dart", $$"""
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../bloc/home_cubit.dart';
import '../bloc/home_state.dart';

class HomePage extends StatelessWidget {
  const HomePage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('{{title}}')),
      body: BlocBuilder<HomeCubit, HomeState>(
        builder: (context, state) {
          switch (state.status) {
            case HomeStatus.loading:
              return const Center(child: CircularProgressIndicator());
            case HomeStatus.error:
              return Center(child: Text('Error: ${state.message}'));
            case HomeStatus.loaded:
              return ListView(
                children: state.items
                    .map((i) => ListTile(leading: const Icon(Icons.bolt), title: Text(i)))
                    .toList(),
              );
            case HomeStatus.initial:
              return const SizedBox.shrink();
          }
        },
      ),
      floatingActionButton: FloatingActionButton(
        onPressed: () => context.read<HomeCubit>().load(),
        child: const Icon(Icons.refresh),
      ),
    );
  }
}
""");
        }
    }
}

import 'package:flutter/material.dart';

import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import '../widgets/tiles.dart';

class HallsScreen extends StatelessWidget {
  const HallsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = AppScope.of(context);
    final halls = state.halls;

    return Scaffold(
      appBar: AppBar(title: const Text('Halls')),
      body: halls.isEmpty
          ? const EmptyState(
              icon: Icons.map_outlined,
              title: 'No halls published yet',
            )
          : RefreshIndicator(
              onRefresh: state.refresh,
              child: ListView.separated(
                itemCount: halls.length,
                separatorBuilder: (_, __) => const Divider(indent: 72),
                itemBuilder: (context, index) => HallTile(hall: halls[index]),
              ),
            ),
    );
  }
}

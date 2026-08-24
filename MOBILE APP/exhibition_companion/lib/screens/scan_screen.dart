import 'dart:async';

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';
import 'exhibitor_detail_screen.dart';

/// Scanning a stand's QR code to request its e-catalogue.
///
/// The whole point of the code resolving to the exhibition system rather than to
/// the exhibitor's own site is that the request gets recorded against a visitor
/// first — that is what turns thirty downloads during the day into one pack that
/// evening. So this screen's job is: read the code, send it, and say clearly
/// what happened.
///
/// It deliberately keeps scanning after a hit. Visitors walk a row of stands and
/// scan four in ninety seconds, and a scanner that has to be reopened between
/// each one turns that into eight taps.
class ScanScreen extends StatefulWidget {
  const ScanScreen({super.key});

  @override
  State<ScanScreen> createState() => _ScanScreenState();
}

class _ScanScreenState extends State<ScanScreen> with WidgetsBindingObserver {
  final MobileScannerController _controller = MobileScannerController(
    detectionSpeed: DetectionSpeed.noDuplicates,
    formats: const [BarcodeFormat.qrCode],
  );

  bool _busy = false;
  ScanResult? _lastResult;
  String? _error;

  /// Guards against the same physical code being posted twice while the phone
  /// is still pointed at it and the first request is in flight.
  String? _inFlightToken;
  Timer? _resetTimer;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    _resetTimer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    _controller.dispose();
    super.dispose();
  }

  /// The camera must stop when the app goes to the background — both stores
  /// treat a camera left running as a privacy problem, and it drains a battery
  /// that has to last a full show day.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    switch (state) {
      case AppLifecycleState.resumed:
        unawaited(_controller.start());
      case AppLifecycleState.inactive:
      case AppLifecycleState.paused:
      case AppLifecycleState.hidden:
      case AppLifecycleState.detached:
        unawaited(_controller.stop());
    }
  }

  Future<void> _onDetect(BarcodeCapture capture) async {
    if (_busy) return;

    String? raw;
    for (final barcode in capture.barcodes) {
      final value = barcode.rawValue?.trim();
      if (value != null && value.isNotEmpty) {
        raw = value;
        break;
      }
    }

    if (raw == null || raw == _inFlightToken) return;

    setState(() {
      _busy = true;
      _error = null;
      _inFlightToken = raw;
    });

    final state = AppScope.read(context);

    try {
      final result = await state.api.scan(raw);
      if (!mounted) return;

      state.setCatalogueCount(result.todayCount);
      setState(() {
        _lastResult = result;
        _busy = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _lastResult = null;
        _busy = false;
      });
    }

    // Let the same code be scanned again after a moment, so a visitor who is
    // not sure it worked can simply point at it again.
    _resetTimer?.cancel();
    _resetTimer = Timer(const Duration(seconds: 3), () {
      if (mounted) setState(() => _inFlightToken = null);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Scan for e-catalogue'),
        actions: [
          IconButton(
            tooltip: 'Torch',
            icon: const Icon(Icons.flashlight_on_outlined),
            onPressed: () => unawaited(_controller.toggleTorch()),
          ),
          IconButton(
            tooltip: 'Switch camera',
            icon: const Icon(Icons.cameraswitch_outlined),
            onPressed: () => unawaited(_controller.switchCamera()),
          ),
        ],
      ),
      body: Column(
        children: [
          Expanded(
            child: Stack(
              alignment: Alignment.center,
              children: [
                MobileScanner(
                  controller: _controller,
                  onDetect: _onDetect,
                  errorBuilder: (context, error, child) => _CameraProblem(
                    error: error,
                    onRetry: () => unawaited(_controller.start()),
                  ),
                ),
                // A plain reticle rather than a decorated overlay: the visitor
                // needs to know where to point, and anything more competes with
                // the code itself for attention.
                IgnorePointer(
                  child: Container(
                    width: 230,
                    height: 230,
                    decoration: BoxDecoration(
                      border: Border.all(color: Colors.white70, width: 3),
                      borderRadius: BorderRadius.circular(20),
                    ),
                  ),
                ),
                if (_busy)
                  Container(
                    color: Colors.black38,
                    child: const Center(child: CircularProgressIndicator()),
                  ),
                Positioned(
                  bottom: 16,
                  left: 24,
                  right: 24,
                  child: Text(
                    'Point at the QR code on the stand',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: Colors.white,
                      shadows: const [Shadow(blurRadius: 6)],
                    ),
                  ),
                ),
              ],
            ),
          ),
          _resultPanel(theme),
        ],
      ),
    );
  }

  Widget _resultPanel(ThemeData theme) {
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(16),
        child: MessageBanner(message: _error!, kind: MessageKind.error),
      );
    }

    final result = _lastResult;
    if (result == null) {
      return const Padding(
        padding: EdgeInsets.all(16),
        child: MessageBanner(
          message: 'Everything you scan today is collected into one pack and '
              'emailed to you this evening. Nothing downloads now.',
          kind: MessageKind.info,
          icon: Icons.inbox_outlined,
        ),
      );
    }

    final added = result.outcome == ScanOutcome.added;

    return Material(
      color: theme.colorScheme.surface,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            MessageBanner(
              message: result.message,
              kind: added ? MessageKind.good : MessageKind.warning,
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(result.stand.exhibitorName,
                          style: theme.textTheme.titleMedium),
                      Text(
                        '${result.stand.hallName} · Stand ${result.stand.standNumber}',
                        style: theme.textTheme.bodySmall,
                      ),
                      Text(
                        '${result.todayCount} on your list today',
                        style: theme.textTheme.bodySmall
                            ?.copyWith(color: theme.colorScheme.primary),
                      ),
                    ],
                  ),
                ),
                TextButton(
                  onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => ExhibitorDetailScreen(
                        exhibitorId: result.stand.exhibitorId,
                      ),
                    ),
                  ),
                  child: const Text('Open'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// What to show when the camera cannot be used.
///
/// A permanently denied permission needs different words from a camera that is
/// simply busy, because the fix is different: one is a trip to Settings, the
/// other is a retry.
class _CameraProblem extends StatelessWidget {
  const _CameraProblem({required this.error, required this.onRetry});

  final MobileScannerException error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final denied =
        error.errorCode == MobileScannerErrorCode.permissionDenied;

    return ColoredBox(
      color: Colors.black,
      child: EmptyState(
        icon: denied ? Icons.no_photography_outlined : Icons.videocam_off_outlined,
        title: denied ? 'Camera access is off' : 'The camera is not available',
        detail: denied
            ? 'The app needs the camera to read stand QR codes. Turn it on for '
                'this app in your phone’s Settings, then come back.'
            : 'Close any other app using the camera and try again. You can also '
                'add exhibitors to your list from their page in Search.',
        actionLabel: denied ? null : 'Try again',
        onAction: denied ? null : onRetry,
      ),
    );
  }
}

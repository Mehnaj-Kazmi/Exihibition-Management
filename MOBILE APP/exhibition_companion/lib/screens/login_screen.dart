import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../api/api_client.dart';
import '../state/app_scope.dart';
import '../widgets/message_banner.dart';

/// Signing in with the address the visitor registered with.
///
/// Two steps on one screen rather than two routes, because the second step is
/// meaningless without the first and a visitor who mistypes their address needs
/// to go back to it without losing their place.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _emailController = TextEditingController();
  final _codeController = TextEditingController();
  final _emailFocus = FocusNode();
  final _codeFocus = FocusNode();

  bool _codeSent = false;
  bool _busy = false;
  String? _error;
  String? _notice;

  /// Only ever set when the server is not sending real email, in which case it
  /// is shown on screen — otherwise the app could not be signed in to at all
  /// before SMTP is configured.
  String? _developmentCode;

  Timer? _resendTimer;
  int _resendIn = 0;

  @override
  void initState() {
    super.initState();
    _emailController.text = AppScope.read(context).lastEmail ?? '';
  }

  @override
  void dispose() {
    _resendTimer?.cancel();
    _emailController.dispose();
    _codeController.dispose();
    _emailFocus.dispose();
    _codeFocus.dispose();
    super.dispose();
  }

  bool get _emailLooksValid {
    final value = _emailController.text.trim();
    return value.contains('@') && value.contains('.') && value.length > 5;
  }

  Future<void> _requestCode() async {
    if (!_emailLooksValid) {
      setState(() => _error = 'Enter the email address you registered with.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });

    try {
      final result =
          await AppScope.read(context).requestCode(_emailController.text);

      if (!mounted) return;
      setState(() {
        _codeSent = true;
        _notice = result.message;
        _developmentCode = result.developmentCode;
        _busy = false;
      });

      _startResendCountdown();
      _codeFocus.requestFocus();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _busy = false;
      });
    }
  }

  Future<void> _verify() async {
    final code = _codeController.text.trim();
    if (code.length < 6) {
      setState(() => _error = 'Enter the six-digit code from your email.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      // On success the app state switches the whole tree to the home shell, so
      // there is nothing to navigate to here.
      await AppScope.read(context).verifyCode(_emailController.text, code);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _busy = false;
        _codeController.clear();
      });
    }
  }

  /// A visitor who does not get the email will tap "send again" repeatedly and
  /// then hit the server's rate limit, which is a worse experience than being
  /// asked to wait. The countdown makes the wait visible instead.
  void _startResendCountdown() {
    _resendTimer?.cancel();
    setState(() => _resendIn = 45);

    _resendTimer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (!mounted) {
        timer.cancel();
        return;
      }
      setState(() => _resendIn--);
      if (_resendIn <= 0) timer.cancel();
    });
  }

  void _changeEmail() {
    _resendTimer?.cancel();
    setState(() {
      _codeSent = false;
      _codeController.clear();
      _developmentCode = null;
      _error = null;
      _notice = null;
      _resendIn = 0;
    });
    _emailFocus.requestFocus();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final state = AppScope.of(context);

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 32),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Icon(Icons.qr_code_scanner,
                      size: 56, color: theme.colorScheme.primary),
                  const SizedBox(height: 20),
                  Text(
                    'Exhibition Companion',
                    style: theme.textTheme.headlineSmall
                        ?.copyWith(fontWeight: FontWeight.w600),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    _codeSent
                        ? 'Enter the six-digit code we sent to\n${_emailController.text.trim()}'
                        : 'Sign in with the email address you registered with.',
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 28),

                  if (state.signedOutBecause != null && !_codeSent)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 16),
                      child: MessageBanner(
                        message: state.signedOutBecause!,
                        kind: MessageKind.warning,
                      ),
                    ),

                  if (!_codeSent) ..._emailStep(theme) else ..._codeStep(theme),

                  if (_error != null) ...[
                    const SizedBox(height: 16),
                    MessageBanner(message: _error!, kind: MessageKind.error),
                  ],

                  const SizedBox(height: 28),
                  _ServerRow(baseUrl: state.baseUrl, onChanged: _promptForServer),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _emailStep(ThemeData theme) => [
        TextField(
          controller: _emailController,
          focusNode: _emailFocus,
          autofocus: true,
          enabled: !_busy,
          keyboardType: TextInputType.emailAddress,
          autofillHints: const [AutofillHints.email],
          textInputAction: TextInputAction.go,
          decoration: const InputDecoration(
            labelText: 'Registered email address',
            prefixIcon: Icon(Icons.alternate_email),
          ),
          onChanged: (_) => setState(() {}),
          onSubmitted: (_) => _requestCode(),
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _busy ? null : _requestCode,
          child: _busy
              ? const SizedBox(
                  height: 20,
                  width: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text('Send me a code'),
        ),
        const SizedBox(height: 12),
        Text(
          'Not registered? Ask at the registration desk — the app uses the same '
          'address as your badge.',
          style: theme.textTheme.bodySmall
              ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          textAlign: TextAlign.center,
        ),
      ];

  List<Widget> _codeStep(ThemeData theme) => [
        if (_notice != null) ...[
          MessageBanner(message: _notice!, kind: MessageKind.info),
          const SizedBox(height: 16),
        ],
        if (_developmentCode != null) ...[
          MessageBanner(
            message:
                'This system is not sending email yet, so here is your code: '
                '$_developmentCode',
            kind: MessageKind.warning,
          ),
          const SizedBox(height: 16),
        ],
        TextField(
          controller: _codeController,
          focusNode: _codeFocus,
          autofocus: true,
          enabled: !_busy,
          keyboardType: TextInputType.number,
          autofillHints: const [AutofillHints.oneTimeCode],
          textInputAction: TextInputAction.go,
          maxLength: 6,
          style: theme.textTheme.headlineSmall?.copyWith(letterSpacing: 12),
          textAlign: TextAlign.center,
          inputFormatters: [FilteringTextInputFormatter.digitsOnly],
          decoration: const InputDecoration(
            counterText: '',
            hintText: '000000',
          ),
          onChanged: (value) {
            setState(() {});
            // Six digits is the whole code, so there is nothing left to wait
            // for — submitting saves every visitor a tap.
            if (value.length == 6 && !_busy) _verify();
          },
          onSubmitted: (_) => _verify(),
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _busy ? null : _verify,
          child: _busy
              ? const SizedBox(
                  height: 20,
                  width: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text('Sign in'),
        ),
        const SizedBox(height: 8),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            TextButton(
              onPressed: _busy ? null : _changeEmail,
              child: const Text('Change email'),
            ),
            TextButton(
              onPressed: (_busy || _resendIn > 0) ? null : _requestCode,
              child: Text(_resendIn > 0 ? 'Send again in ${_resendIn}s' : 'Send again'),
            ),
          ],
        ),
      ];

  Future<void> _promptForServer() async {
    final controller =
        TextEditingController(text: AppScope.read(context).baseUrl);

    final entered = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Exhibition system address'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'The web address of the exhibition system. The organiser can tell '
              'you this — it is the same address the stand QR codes point to.',
            ),
            const SizedBox(height: 16),
            TextField(
              controller: controller,
              autofocus: true,
              keyboardType: TextInputType.url,
              decoration: const InputDecoration(
                hintText: 'https://exhibition.example.com',
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, controller.text.trim()),
            child: const Text('Save'),
          ),
        ],
      ),
    );

    if (entered == null || entered.isEmpty || !mounted) return;

    await AppScope.read(context).setBaseUrl(entered);
    if (!mounted) return;

    setState(() {
      _codeSent = false;
      _error = null;
      _notice = null;
    });
  }
}

class _ServerRow extends StatelessWidget {
  const _ServerRow({required this.baseUrl, required this.onChanged});

  final String baseUrl;
  final Future<void> Function() onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Flexible(
          child: Text(
            baseUrl,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
          ),
        ),
        TextButton(onPressed: onChanged, child: const Text('Change')),
      ],
    );
  }
}

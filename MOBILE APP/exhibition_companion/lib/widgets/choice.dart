/// What a filter sheet hands back, and the row a visitor taps to choose it.
library;

import 'package:flutter/material.dart';

/// What a filter sheet hands back.
///
/// A bare `T?` cannot distinguish "the visitor chose Any" from "the visitor
/// swiped the sheet away", and those must do different things — one clears the
/// filter, the other leaves it exactly as it was. Wrapping the value makes the
/// difference `null` versus `Choice(null)`, which the compiler can check.
class Choice<T> {
  const Choice(this.value);

  final T? value;
}

/// One option in a filter sheet.
///
/// Deliberately not a `RadioListTile`. These sheets close on the first tap, so
/// nothing is ever managing a radio group — the circle is only ever a picture
/// of what is currently chosen. Drawing it directly says what is actually
/// happening, and it sidesteps Flutter's radio-group API, which was deprecated
/// after 3.32 and whose replacement would have forced this app's minimum
/// Flutter version up for no gain to anyone.
class FilterOptionTile extends StatelessWidget {
  const FilterOptionTile({
    super.key,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colours = Theme.of(context).colorScheme;

    return ListTile(
      leading: Icon(
        selected ? Icons.radio_button_checked : Icons.radio_button_unchecked,
        color: selected ? colours.primary : colours.outline,
      ),
      title: Text(label),
      selected: selected,
      onTap: onTap,
    );
  }
}

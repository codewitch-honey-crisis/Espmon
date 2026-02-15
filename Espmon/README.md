# Path Autocomplete Controls for WinUI3

Two implementations of a path/regex autocomplete control for your query designer.

## Controls

### 1. PathAutocompleteBox (ListView Dropdown)
Uses a custom TextBox with a Popup containing a ListView for suggestions.

**Pros:**
- More control over dropdown behavior
- Explicit popup positioning
- Cleaner separation of input and suggestions

**Cons:**
- More code to manage popup state
- Manual focus handling

### 2. PathAutoSuggestBox (AutoSuggestBox)
Uses WinUI's built-in AutoSuggestBox control.

**Pros:**
- Built-in dropdown behavior
- Less code to maintain
- Native WinUI styling and keyboard navigation
- Visual feedback with icons in QueryIcon

**Cons:**
- Less customization flexibility
- Header visibility behavior

## Features (Both Controls)

✅ **Regex Pattern Detection**: Detects patterns wrapped in single quotes `'regex'`
✅ **Quote/Bracket Validation**: Validates quote pairing and bracket nesting
✅ **Real-time Matching**: Shows matching paths as you type
✅ **Match Count Display**: Shows number of matches for regex patterns
✅ **Visual Validation**: Color-coded borders/icons for validation state
✅ **Pattern Validation Event**: Fires event for custom regex validation
✅ **Two-Way Binding**: Exposes SelectedPath for binding
✅ **Performance Limiting**: Limits to 50 matches with timeout protection

## Usage

### Basic XAML

```xml
<controls:PathAutocompleteBox
    AvailablePaths="{x:Bind ViewModel.Paths, Mode=OneWay}"
    SelectedPath="{x:Bind ViewModel.SelectedPath, Mode=TwoWay}"
    PatternValidation="OnPatternValidation"
    Width="400"/>
```

### Handling Validation Event

```csharp
private void OnPatternValidation(object sender, PatternValidationEventArgs e)
{
    if (e.IsRegex)
    {
        try
        {
            // Extract regex from between quotes
            string pattern = ExtractRegex(e.Pattern);
            var regex = new Regex(pattern);
            e.IsValid = true;
        }
        catch (ArgumentException ex)
        {
            e.IsValid = false;
            e.ErrorMessage = ex.Message;
        }
    }
}
```

## Pattern Syntax

### Plain Paths
```
/cpu/0/core/0/load
/gpu/0/temperature
```

### Regex Patterns
```
'^/cpu/[0-1]/core/[0-9]+/load$'
'^/gpu/\d+/temperature$'
```

**Escaping in Regex:**
- Single quotes inside regex: `\'` (unless in character class `[]`)
- Backslashes: normal regex escaping applies
- Character classes: `[0-9]`, `[a-z]`, etc.

## Behavior

### Plain Path Mode
- Input: `/cpu/0/core`
- Shows: All paths containing that substring
- On Selection: Replaces input with selected path
- Updates: `SelectedPath` property

### Regex Pattern Mode  
- Input: `'^/cpu/[0-1]/core/[0-9]+/load$'`
- Shows: All paths matching the regex
- On Selection: Keeps regex in textbox (doesn't replace)
- Displays: Match count indicator
- Updates: Does NOT update `SelectedPath` (only updates for plain paths)

## Properties

### Dependency Properties
- `AvailablePaths` (ObservableCollection<string>): Collection of available paths
- `SelectedPath` (string): Currently selected/entered path (plain paths only)

### Read-Only Properties
- `PathPattern` (string): Current text in the input box
- `MatchingPaths` (ObservableCollection<string>): Current matches
- `IsRegexPattern` (bool): Whether current input is a regex
- `MatchCountText` (string): Display text for match count

### Events
- `PatternValidation`: Fires when pattern needs validation
  - `Pattern`: The pattern to validate
  - `IsRegex`: Whether it's a regex pattern
  - `IsValid`: Set this to indicate validity
  - `ErrorMessage`: Optional error message

## Validation States

### PathAutocompleteBox (ListView)
- **Default**: Gray border
- **Valid Regex**: Green border
- **Invalid**: Red border

### PathAutoSuggestBox  
- **Default**: Search icon (gray)
- **Valid Regex**: Checkmark icon (green)
- **Invalid**: Error icon (red)
- **Timeout/Warning**: Warning icon (orange)

## Testing

The `TestPage` demonstrates both controls side-by-side with:
- Sample hardware monitoring paths
- Real-time validation logging
- Independent state for each control
- Example regex patterns to try

### Example Patterns to Test

```
Plain paths:
- /cpu
- core/0
- gpu/0/load

Regex patterns:
- '^/cpu/[0-1]/core/[0-9]+/load$'
- '^/gpu/\d+/.*$'
- '^/(cpu|gpu)/\d+/.*$'

Invalid patterns:
- 'unclosed regex
- '[unclosed bracket'
- 'invalid\\'escape'
```

## Next Steps

Once you've chosen which implementation you prefer, you can:
1. Integrate it into your query builder layout
2. Create additional controls for operators (avg, sum, etc.)
3. Create controls for arithmetic operations
4. Build a composite control that combines these pieces
5. Add serialization/deserialization for saving queries

Let me know which approach you prefer and what control you'd like to build next!

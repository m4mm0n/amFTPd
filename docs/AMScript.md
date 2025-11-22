# AMScript Engine

AMScript provides:
- Per-user rules
- Per-group rules
- FXP/ActiveMode policies
- Runtime evaluation

## Example Rule
```
if (UserGroup == "Staff") {
  AllowDownload = true;
}
```

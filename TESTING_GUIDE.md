# Env Manager - Local Testing Guide

This document provides instructions for testing the Env Manager CLI and GUI locally before deployment.

## Prerequisites

- Windows 10 21H2 or later (Windows 11 recommended)
- .NET 10 SDK (for CLI testing)
- Node.js 20.x and npm (for GUI testing)

## Part 1: CLI Testing

### 1.1 Basic Commands

Test the core CLI functionality:

```powershell
# Navigate to the CLI binary location
cd .\bin\Release\net10.0\

# Display help
.\env-manager.exe help

# List all environment variables (user scope)
.\env-manager.exe list

# Get a specific variable
.\env-manager.exe get PATH

# Get a system variable (requires admin prompt)
.\env-manager.exe get PATH --scope system
```

### 1.2 Set/Delete Variables

Test variable manipulation (these are safe operations on non-critical variables):

```powershell
# Set a test variable
.\env-manager.exe set TEST_VAR "test_value"

# Verify it was set
.\env-manager.exe get TEST_VAR

# Update the test variable
.\env-manager.exe set TEST_VAR "updated_value"

# Delete the test variable
.\env-manager.exe delete TEST_VAR

# Verify it was deleted
.\env-manager.exe get TEST_VAR
```

### 1.3 Backup and Restore

Test backup/restore workflow:

```powershell
# Create a backup
.\env-manager.exe backup --output env-backup.json

# View the backup file (should be valid JSON)
cat env-backup.json

# Validate the backup
.\env-manager.exe validate env-backup.json

# Create another backup for diff testing
Start-Sleep -Seconds 2
.\env-manager.exe backup --output env-backup-2.json

# Compare backups
.\env-manager.exe diff env-backup.json env-backup-2.json

# Test merge
.\env-manager.exe merge env-backup.json env-backup-2.json --output env-merged.json

# Verify merged file
.\env-manager.exe validate env-merged.json
```

### 1.4 Error Handling

Test error conditions:

```powershell
# Test non-existent variable
.\env-manager.exe get NONEXISTENT_VAR_12345

# Test invalid scope
.\env-manager.exe list --scope invalid

# Test invalid backup file
.\env-manager.exe validate nonexistent.json

# Test restore with insufficient permissions (system scope without admin)
.\env-manager.exe set ADMIN_TEST "value" --scope system
```

## Part 2: GUI Testing (Frontend Assets)

The GUI is currently available as frontend assets in the `dist/` folder:

```powershell
# Check that frontend build succeeded
dir ./dist/
# Should see: index.html, assets/

# Open in browser (direct HTML testing)
start ./dist/index.html
```

### 2.1 Manual GUI Testing Checklist

When the GUI opens in browser:

- [ ] Page loads without JavaScript errors (check browser console)
- [ ] Environment variables list is populated
- [ ] Search/filter works
- [ ] Scope selector (user/system) changes UI state
- [ ] Add/Edit/Delete buttons are visible and clickable
- [ ] Backup/Restore buttons are present
- [ ] Responsive design works on different window sizes

## Part 3: Integration Testing

Test CLI-GUI integration (manual):

```powershell
# Set a test variable via CLI
.\env-manager.exe set GUI_TEST_VAR "gui_test_value"

# Verify in GUI (reload page) - should see the new variable

# Delete via GUI (when available)
# Verify deletion via CLI
.\env-manager.exe get GUI_TEST_VAR
```

## Part 4: Performance Testing

Test performance on a large number of variables:

```powershell
# Backup current state
.\env-manager.exe backup --output perf-test-baseline.json

# Note: Do not test adding many variables without reverting first

# Test list performance
Measure-Command {
    .\env-manager.exe list | Out-Null
}

# Test diff performance on large backups
Measure-Command {
    .\env-manager.exe diff perf-test-baseline.json perf-test-baseline.json | Out-Null
}
```

## Part 5: Security Testing

### 5.1 Scope Isolation

```powershell
# Verify user scope doesn't affect system scope
.\env-manager.exe set SCOPE_TEST "user_value"
.\env-manager.exe get SCOPE_TEST
# Output: user_value

# Try to read from system (without admin, should fail or show nothing)
.\env-manager.exe get SCOPE_TEST --scope system
```

### 5.2 Input Validation

```powershell
# Test very long variable names (limit: 255 chars)
$longName = "A" * 260
.\env-manager.exe set $longName "value"

# Test very long values (limit: 32767 chars)
$longValue = "X" * 33000
.\env-manager.exe set TEST $longValue
```

## Part 6: Cleanup

After testing, clean up test variables and files:

```powershell
# Remove test variables
.\env-manager.exe delete TEST_VAR -ErrorAction SilentlyContinue
.\env-manager.exe delete GUI_TEST_VAR -ErrorAction SilentlyContinue
.\env-manager.exe delete SCOPE_TEST -ErrorAction SilentlyContinue

# Remove test files
Remove-Item env-backup.json -ErrorAction SilentlyContinue
Remove-Item env-backup-2.json -ErrorAction SilentlyContinue
Remove-Item env-merged.json -ErrorAction SilentlyContinue
Remove-Item perf-test-baseline.json -ErrorAction SilentlyContinue
```

## Reporting Issues

If you encounter any issues during testing:

1. **CLI Issues**: Note the exact command and error message
2. **GUI Issues**: Check browser console for errors (F12)
3. **Performance**: Record timing measurements
4. **Security**: Note any unexpected access to variables

Document findings in the GitHub Issues section with:
- Windows version
- CLI/GUI version (from --version or GUI footer)
- Exact reproduction steps
- Expected vs. actual behavior
- Screenshots (for GUI issues)

## Next Steps

After local testing is complete and verified:

1. Report findings to the development team
2. Wait for fixes if critical issues found
3. Proceed with GitHub Actions CI/CD testing (automatic on next push)
4. Prepare for release when all tests pass

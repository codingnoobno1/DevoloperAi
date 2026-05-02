```
@echo off
:: Create new Python project directory
mkdir hello
cd hello

:: Create new virtual environment
python -m venv env

:: Activate the virtual environment
env\Scripts\activate

:: Print a message to indicate successful activation
echo Virtual environment activated!

:: Pause to keep the terminal open
pause
```
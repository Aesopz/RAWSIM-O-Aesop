@echo off
REM NTUST thesis build: xelatex -> bibtex -> xelatex -> xelatex
REM latexmk ???(MiKTeX ?? Perl),?????????????
setlocal
set "MIKTEX=C:\Users\Aesop\AppData\Local\Programs\MiKTeX\miktex\bin\x64"
set "PATH=%MIKTEX%;%PATH%"
cd /d "%~dp0"
xelatex -interaction=nonstopmode my_ntust_thesis.tex
bibtex my_ntust_thesis
xelatex -interaction=nonstopmode my_ntust_thesis.tex
xelatex -interaction=nonstopmode my_ntust_thesis.tex
echo.
echo ===== done: my_ntust_thesis.pdf =====

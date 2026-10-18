return require('return {1, 2}'), pcall(require, ''), pcall(require, '-x'),
pcall(require, 'return require("return 1")')

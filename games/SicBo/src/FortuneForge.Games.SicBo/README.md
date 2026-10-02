# Sic Bo

Sic Bo is a three-die game. This package models rolls and the supported Small, Big, Odd, Even, Single Number, Total, Two Number Combination, Specific Double, Any Triple, and Specific Triple bets.

The selected electronic-table GRA/RWS-style profit odds are: Small/Big/Odd/Even 1:1; Single Number 1:1, 2:1, or 12:1 by occurrence count; totals 4/17 64:1, 5/16 32:1, 6/15 19:1, 7/14 12:1, 8/13 8.5:1, 9/12 7:1, and 10/11 6.5:1; Two Number Combination 6:1; Specific Double 11.5:1; Any Triple 32:1; and Specific Triple 195:1. Small, Big, Odd, and Even all lose on a triple.

`SicBoPaytable` accepts an explicit `SicBoRoll` for deterministic hosts and tests. Hosts own secure dice generation and randomness.

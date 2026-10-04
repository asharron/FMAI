Ram values:

Loyd:
Max HP: 00D580

Position: 00D540 -> 

00D548
00D54A

Sakata:
00D600
00D610

Enemy?
00D849


-- Probably same size
011B9A
Movement bitmask rows? 45 + 6 bytes?

01CB9A
Movement cost grid? 46 + 6 bytes

574 -> 4
35 -> 21

Know x,y coordinates to movement grid:

(x, y) => (row, col)

# Diagonal movement
(0, 23) => (44, 3)
(1, 22) => (42, 6)
(2, 22) => (42, 7)
(3, 21) => (40, 10)
(4, 21) => (40, 11)
(5, 20) => (38, 14)
(6, 20) => (38, 15)
(7, 19) => (37, 2)
(8, 19) => (37, 3)
(9, 18) => (35, 6)
(10, 18) => (35, 7)
(11, 17) => (33, 10)
(12, 17) => (33, 11)
(13, 16) => (31, 14)
(14, 16) => (31, 15)
(15, 15) => (30, 2)
(16, 15) => (30, 3)


# Up down movement
(6, 19) => (37, 1)
(6, 20) => (38, 15)
(6, 21) => (40, 13)
(6, 22) => (42, 11)

# Left right movement
(6, 19) => (37, 1)
(8, 19) => (37, 3)
(10, 19) => (37, 5)
(12, 19) => (37, 7)

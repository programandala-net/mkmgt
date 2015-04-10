#! /usr/bin/env gforth

\ bin2mgt.fs
\ A MGT disk image creator
\ for ZX Spectrum's GDOS, G+DOS and Beta DOS.
\ http://programandala.net/en.program.bin2mgt.html
\
\ Written in Forth with Gforth
\ by Marcos Cruz (programandala.net).
\
\ Algorithms based on:
\   pyz80 by Andrew Collier, version 1.2 2-Feb-2009
\   http://www.intensity.org.uk/samcoupe/pyz80.html

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ History

\ 2015-04-10: start.

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Requirements

require string.fs \ Gforth dynamic strings

require galope/ends-question.fs  \ 'ends?'

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Disk image

variable mgt-filename$

2   constant sides/disk
80  constant tracks/side
10  constant sectors/track
512 constant bytes/sector
80  constant files/disk

sides/disk tracks/side sectors/track bytes/sector * * *
constant bytes/image

bytes/image allocate throw constant image


: geometry>pos  ( track side sector -- u )
  \ Convert a track, a side and a sector
  \ to a position in the disk image.
  \ track   = 0..79
  \ side    = 0..1
  \ sector  = 1..10
  1-  swap 10 * +  swap 20 * +  bytes/sector *
  ;

: entry>pos  ( entry -- +n )
  \ Convert a directory entry (0..79)
  \ to its position in the disk image.
  >r r@ 20 /       \ track
  0                \ side
  r@ 20 mod 2/ 1+  \ sector
  geometry>pos
  r> 2 mod 256 * +
  ;

: @z80 ( a -- n )
  \ Fetch a 16-bit value with Z80 format.
  dup c@ swap c@ 256 * + 
  ;
: !z80 ( n a -- )
  \ Store a 16-bit value with Z80 format: LSB first.
  2dup swap 256 mod swap c!
  swap 256 / swap c!
  ;
: @bigendian ( a -- n )
  \ Fetch a 16-bit value with big-endian format: MSB first.
  dup c@ 256 * swap c@ +
  ;
: !bigendian ( a -- n )
  \ Store a 16-bit value with big-endian format: MSB first.
  2dup swap 256 / swap c!
  swap 256 mod swap c!
  ;

\ Disk image fetch and store

: mgtc@   ( +n -- 8b )   image + c@  ;
: mgtc!   ( 8b +n -- )   image + c!  ;
: mgt@    ( +n -- 16b )  image + @z80  ;
: mgt@be  ( +n -- 16b )  image + @bigendian  ;
: mgt!    ( 16b +n -- )  image + !z80  ;
: mgt!be  ( +n -- 16b )  image + !bigendian  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Parameters

: +extension  ( ca1 len1 -- ca2 len2 )
  \ Add the .mgt file extension to the given filename, if missing.
  s" .mgt" ends? 0= if  s" .mgt" s+  then
  ;
: get-mgt-filename  ( -- )
  1 arg  +extension mgt-filename $!
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Files

variable sectors_already_used

: free-entry?  ( -- +n true | false ) 
  false  \ default output
  sectors_already_used off
  files/disk 0 ?do
    i entry>pos dup c@
    if    11 + mgt@be sectors_already_used +!
    else  swap 0= unloop exit  then
  loop
  ;

: file-length  ( ca len -- n )
  r/o open-file throw
  dup file-size throw d>s
  swap close-file throw
  ;

variable sectors/file
variable starting_side
variable starting_track
variable starting_sector

: (file>mgt)  ( ca len +n -- )
  \ Copy a file to the disk image.
  \ ca len = filename
  \ +n = image position of a free directory entry
  dup 9 mgtc!  \ code file type

  \ XXX TODO
  2dup file-length 9 + 510 / 1+ dup sectors/file !
  11 img!be
   
  sectors_already_used @ 10 / 4 + dup
  80 /    starting_side !
  80 mod  starting_track !
  sectors_already_used @ 10 mod 1+ starting_sector !

  starting_track @ starting_side 128 and + 13 mgtc!
  starting_sector @ 14 mgtc!

  \ 15 - 209 sector address map
  \ XXX TODO

  \ GDOS header
  \ XXX TODO

  \ Save file
  \ XXX TODO

  ;

: file>mgt  ( ca len -- )
  \ Copy a file to the disk image, if there's a free directory entry.
  \ ca len = filename
  free-entry? if  (file>mgt)
  else  abort" Too many files for MGT format"  then
  ;
: files>mgt  ( -- )
  \ Copy the parameter files to the disk image.
  argc @ 2 ?do  i arg file>mgt  loop
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Usage

: .command  ( -- )
  \ Print the name of this file.
  cr  s"   bin2mgt.fs" type
  ;
: usage  ( -- )
  \ Show the usage instructions.
  cr ." Usage:" cr
  cr ." The first parameter is the MGT disk image filename."
  cr ." Its extension '.mgt' will be automatically added if missing."
  cr ." WARNING: If the file already exists, it will be overwritten." cr
  cr ." The next parameters are files to be added to the disk image"
  cr ." (shell patterns can be used)." cr
  cr ." Examples:" cr
  command ."  mynewdiskimage myfile.bin"
  command ."  mynewdiskimage myfile1.bin myfile2.txt"
  command ."  mynewdiskimage img*.png data??.*"
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Boot

: check  ( -- )
  \ Make sure the number of parameters is 3 or more.
  argc @ 3 < if  usage bye  then
  ;
: run  ( -- )
  check get-mgt-filename files>mgt
  ;

\ run  bye

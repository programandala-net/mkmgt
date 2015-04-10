#! /usr/bin/env gforth

\ mkmgt

\ A MGT disk image creator
\ for ZX Spectrum's GDOS, G+DOS and Beta DOS.
\ http://programandala.net/en.program.mkmgt.html
\
\ Copyright (C) 2015 Marcos Cruz (programandala.net)

\ mkmgt is free software; you can redistribute it and/or modify it
\ under the terms of the GNU General Public License as published by
\ the Free Software Foundation; either version 3 of the License, or
\ (at your option) any later version.
\
\ mkmgt is distributed in the hope that it will be useful, but WITHOUT
\ ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
\ or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public
\ License for more details.
\
\ You should have received a copy of the GNU General Public License
\ along with mkmgt; if not, see <http://gnu.org/licenses>.

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Acknowledgements

\ Written in Forth with Gforth
\   http://gnu.org/software/gforth
\
\ MGT disk image algorithms adapted from:
\   pyz80 by Andrew Collier, version 1.2 2-Feb-2009
\   http://www.intensity.org.uk/samcoupe/pyz80.html
\ Information on the MGT filesystem retrieved from:
\   http://scratchpad.wikia.com/wiki/MGT_filesystem

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
10  constant /filename

sides/disk tracks/side sectors/track bytes/sector * * *
constant bytes/image

bytes/image allocate throw constant image
image bytes/image erase

: geometry>pos  ( track side sector -- +n )
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
  \ Fetch a 16-bit value with Z80 format: LSB first.
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

: image+  ( +n -- a )    image +  ;
: mgtc@   ( +n -- 8b )   image+ c@  ;
: mgtc!   ( 8b +n -- )   image+ c!  ;
: mgt@    ( +n -- 16b )  image+ @z80  ;
: mgt@be  ( +n -- 16b )  image+ @bigendian  ;
: mgt!    ( 16b +n -- )  image+ !z80  ;
: mgt!be  ( +n -- 16b )  image+ !bigendian  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Parameters

: +extension  ( ca1 len1 -- ca2 len2 )
  \ Add the .mgt file extension to the given filename, if missing.
  s" .mgt" ends? 0= if  s" .mgt" s+  then
  ;
: get-mgt-filename  ( -- )
  \ Get the first parameter, the MGT disk image filename.
  1 arg  +extension mgt-filename $!
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Files

: -filename  ( a -- )
  \ Clear a DOS filename in the disk image.
  \ a = address in the disk image
  /filename blank
  ;

: filename!  ( ca len a -- )
  \ Store a DOS filename into the disk image.
  \ ca len = filename
  \ a = address in the disk image
  dup -filename
  >r basename /filename min
  r> swap move
  ;

variable sectors-already-used

: free-entry?  ( -- +n true | false ) 
  \ Is there a free directory entry in the disk image?
  \ `sectors-already-used` is calculated in the process.
  \ +n = position in the disk image
  false  \ default output
  sectors-already-used off
  files/disk 0 ?do
    i entry>pos dup mgtc@
    if    11 + mgt@be sectors-already-used +!
    else  swap 0= unloop exit  then
  loop
  ;

: file-length  ( ca len -- n )
  \ Get the length of a file.
  \ ca len = filename with path
  r/o open-file throw
  dup file-size throw d>s
  swap close-file throw
  ;

variable sectors/file  \ number of sectors used by the current file
variable starting-side
variable starting-track
variable starting-sector

variable filename$
variable entry-pos

: (file>mgt)  ( ca len +n -- )

  \ Copy a file to the disk image.

  \ ca len = filename
  \ +n = image position of a free directory entry

  entry-pos !  filename$ $!
  
  \ Set the code file type id (9)
  \ (position 0 of the directory entry).

  9 entry-pos @ mgtc!

  \ Store the filename
  \ (positions 1-10 of the directory entry).

  filename$ $@ entry-pos @ 1+ filename!

  \ Calculate and store the number of sectors used by the file
  \ (positions 11-12 of the directory entry).

  filename$ $@ file-length 9 + 510 / 1+  dup sectors/file !
  entry-pos @ 11 + img!be

  \ Calculate the starting side, track and sector of the file.

  sectors-already-used @ 10 / 4 + dup
  80 /    starting-side !
  80 mod  starting-track !
  sectors-already-used @ 10 mod 1+ starting-sector !

  \ Set the address of the first sector in the file
  \ (positions 13-14 of the directory entry).

  starting-track @ starting-side 128 and +
  entry-pos @ 13 + mgtc!  \ track (0-79, 128-207)
  starting-sector @ entry-pos @ 14 + mgtc!  \ sector (1-10)

  \ Create the sector address map
  \ (positions 15-209 of the directory entry).

  \ The sector address map occupies 195 bytes. A bit is set in this
  \ map if the corresponding sector is allocated to the file. The lsb
  \ of byte 0 corresponds to track 4, sector 1. The msb of byte 0
  \ corresponds to track 4, sector 8. The lsb of byte 1 corresponds to
  \ track 4, sector 9. The msb of byte 1 corresponds to track 5,
  \ sector 6.

  begin  sectors/file @  while

    \ XXX -- original Python code:
    \ image[dirpos+15 + sectors_already_used/8] |= (1 << (sectors_already_used & 7))

    entry-pos @  15 +  sectors-already-used @ 8 / +  \ map address
    dup c@  \ current content
    1 sectors-already-used @ %111 and lshift  \ bit to be set
    or swap c!  \ update

    1 sectors-already-used +!
    -1 sectors/file +!

  repeat

  \ Set the GDOS header
  \ (positions 210-219 of the directory entry)

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

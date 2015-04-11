#! /usr/bin/env gforth

\ mkmgt
\ Version A-00-201504110124

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

\ mkmgt is written in Forth with Gforth:
\   http://gnu.org/software/gforth
\
\ MGT disk image algorithms were adapted from:
\   pyz80 by Andrew Collier, version 1.2 2-Feb-2009
\   http://www.intensity.org.uk/samcoupe/pyz80.html

\ Information on the MGT filesystem was retrieved from:
\   http://scratchpad.wikia.com/wiki/MGT_filesystem

\ Information on the TAP file format was retrieved
\ from the "Z80" ZX Spectrum emulator's documentation.
\ XXX TODO author

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ History

\ 2015-04-10: Start. First working version: A-00-201504102349.

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ To-do

\ Copy TAP files and use the information from their headers.

\ Add files to an existent disk image.

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Requirements

require string.fs \ Gforth dynamic strings

\ From the Galope library
\ (http://programandala.net/en.program.galope.html):

: ends? ( ca1 len1 ca2 len2 -- ca1 len1 wf )
  \ Check end of string:
  \ Is ca2 len2 the end of ca1 len1?
  \ ca1 len1 = long string
  \ ca2 len2 = end to check
  2over  dup 3 pick - /string  compare 0=
  ;

: unslurp-file  ( ca1 len1 ca2 len2 -- )
  \ ca1 len1 = content to write to the file
  \ ca2 len2 = filename
  w/o create-file throw >r
  r@ write-file throw
  r> close-file throw
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Disk image

\ ----------------------------------------------
\ Data

variable image-filename$

  2 constant sides/disk     \ sides (0..1)
 80 constant tracks/side    \ tracks (0..79)
 10 constant sectors/track  \ sectors (1..10)
512 constant bytes/sector   \ sector size
510 constant data/sector    \ actual data saved into a sector
 80 constant files/disk     \ max number of directory entries (1..80)
 10 constant /filename      \ max length of DOS filenames
  9 constant /file-header   \ length of the file header
256 constant /entry         \ size of a directory entry

sides/disk tracks/side sectors/track bytes/sector * * *
constant /image  \ length of the disk image

/image allocate throw constant image
image /image erase

\ ----------------------------------------------
\ Converters

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

: image+  ( +n -- a )
  \ Convert a position in the disk image
  \ to its actual memory address.
  image +
  ;

\ ----------------------------------------------
\ Fetch and store

: @z80 ( a -- 16b )
  \ Fetch a 16-bit value with Z80 format: LSB first.
  dup c@ swap 1+ c@ 256 * +
  ;
: !z80 ( 16b a -- )
  \ Store a 16-bit value with Z80 format: LSB first.
  swap 2dup  256 mod swap c!  256 / swap 1+ c!
  ;
: @big-endian ( a -- 16b )
  \ Fetch a 16-bit value with big-endian format: MSB first.
  dup c@ 256 * swap 1+ c@ +
  ;
: !big-endian ( 16b a -- )
  \ Store a 16-bit value with big-endian format: MSB first.
  swap 2dup  256 / swap c!  256 mod swap 1+ c!
  ;

: mgtc@   ( +n -- 8b )   image+ c@  ;
: mgtc!   ( 8b +n -- )   image+ c!  ;
: mgt@    ( +n -- 16b )  image+ @z80  ;
: mgt@be  ( +n -- 16b )  image+ @big-endian  ;
: mgt!    ( 16b +n -- )  image+ !z80  ;
: mgt!be  ( +n -- 16b )  image+ !big-endian  ;

\ ----------------------------------------------
\ File name

: +extension  ( ca1 len1 -- ca2 len2 )
  \ Add the .mgt file extension to the given filename, if missing.
  s" .mgt" ends? 0= if  s" .mgt" s+  then
  ;
: get-image-filename  ( -- )
  \ Get the first parameter, the MGT disk image filename.
  1 arg  +extension image-filename$ $!
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Files

variable sectors/file  \ number of sectors used by the current file
variable starting-side
variable starting-track
variable starting-sector

variable input-filename$  \ filename of the current file (dinamyc string)
variable entry-pos  \ directory entry position in the disk image

2variable file-contents  \ contents of the current file (memory zone)

\ The `tap-file` variable is zero when the current file is not
\ extracted from a TAP file; otherwise it holds the ordinal number of
\ the actual file inside the TAP file. Thus it is used as a flag and
\ as a counter.

variable tap-file

: filename!  ( ca len +n -- )
  \ Store a DOS filename into the disk image.
  \ ca len = filename
  \ +n = disk image position
  image+ dup >r /filename blank r> swap move
  ;

: tape-header+  ( n -- a )
  \ Convert a position in the tape header to its actual address.
  \ XXX TODO adapt to TAP files containing several files.
  \ XXX TODO rename
  file-contents 2@ drop +
  ;

  \ The tape header id is:
  \ 0 for a BASIC program;
  \ 1 for a number array;
  \ 2 for a character array;
  \ 3 for a code file.
  \ A SCREEN$ file is regarded as a code file with start address 16384 and length 6912 decimal.

: (tape-header-id)  ( -- n )
  \ Tape header id of the current input file, that is TAP file.
  3 tape-header+ c@
  ;
: tape-header-id  ( -- n )
  \ Tape header id of the current input file.
  tap-file @ if  (tape-header-id)  else  3  then
  ;
: file-type  ( -- n )
  \ DOS file type of the current input file.
  \ DOS file types:
  \   0: Erased            1: ZX BASIC    2: ZX numeric array
  \   3: ZX string array   4: ZX code     5: ZX 48K snapshot
  \   6: ZX Microdrive     7: ZX screen   8: Special
  \   9: ZX 128K snapshot 10: Opentype   11: ZX execute
  tap-file @ if  (tape-header-id) 1+  else  4  then
  ;
: dos-filename  ( -- ca len )
  \ DOS filename of the current input file.
  \ ca len = filename
  tap-file @ if    4 tape-header+  /filename
             else  input-filename$ $@ basename /filename min  then
  ;
: file-length  ( -- n )
  \ Length of the current file.
  \ XXX TODO factor
  \ XXX confirmed
  file-contents 2@  tap-file @ if  drop 14 + @z80  else  nip  then
  ;
: start  ( -- n )
  \ Autostart line (for BASIC programs) or start address (for code files)
  \ XXX confirmed
  tap-file @ if  16 tape-header+ @z80  else  0  then
  ;
: start2  ( -- n )
  \ If it's a BASIC program, return the start of the variable area
  \ relative to the start of the program; ff it's a code file, return 32768.
  \ XXX confirmed
  tap-file @ if  18 tape-header+ @z80 else  32768  then
  ;


: file+  ( +n -- a )
  \ Convert a position in the current file
  \ to its actual memory address.
  \ XXX TODO factor with tape-header+
  file-contents 2@ drop +
  tap-file @ if  24 +  then  \ skip the TAP file header
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Directory entry

variable sectors-already-used

: free-entry?  ( -- +n true | false ) 
  \ Is there a free directory entry in the disk image?
  \ `sectors-already-used` is calculated in the process.
  \ +n = position in the disk image
  false  \ default output
  sectors-already-used off
  files/disk 0 ?do
    i entry>pos
    \ dup cr ." entry pos=" .  \ XXX INFORMER
    dup mgtc@
    if    11 + mgt@be
          \ cr ." sectors used by the entry=" dup . \ XXX INFORMER
          sectors-already-used +!
          \ cr ." sectors already used=" sectors-already-used ? \ XXX INFORMER
    else
          swap 0= unloop exit  then
  loop
  ;

: -directory-entry  ( +n -- )
  \ XXX not used
  \ Erase a directory entry in the disk image.
  \ +n = disk image position of a directory entry
  image+ /entry 0xFF fill
  ;

: directory-entry  ( ca len +n -- )

  \ Create a directory entry in the disk image.

  \ ca len = filename (host system format)
  \ +n = disk image position of a free directory entry

  \ dup -directory-entry \ XXX not used

  entry-pos !  input-filename$ $!

  \ Set the file type
  \ (position 0 of the directory entry).

  file-type entry-pos @ mgtc!

  \ Store the filename
  \ (positions 1-10 of the directory entry).

  dos-filename entry-pos @ 1+ filename!

  \ Calculate and store the number of sectors used by the file
  \ (positions 11-12 of the directory entry).

  \ The file header (bytes 211-219 of the directory entry
  \ is saved also at the start of the file for some file types).

  file-length /file-header + 510 / 1+  dup sectors/file !
  \ dup cr ." sectors used by the new file=" . \ XXX INFORMER
  \    cr ." sectors already used=" sectors-already-used ? \ XXX INFORMER
  entry-pos @ 11 + mgt!be

  \ Calculate the starting side, track and sector of the file.

  sectors-already-used @ 10 / 4 + dup
  80 /    starting-side !
  80 mod  starting-track !
  sectors-already-used @ 10 mod 1+ starting-sector !

  \ Set the address of the first sector in the file
  \ (positions 13-14 of the directory entry).

  starting-track @ starting-side @ 128 and +
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

  sectors/file @  0 ?do

    \ XXX -- original Python code:
    \ image[dirpos+15 + sectors_already_used/8] |= (1 << (sectors_already_used & 7))

    entry-pos @  15 +  sectors-already-used @ 8 / +  \ map position
    image+ dup c@  \ address and its current content
    1 sectors-already-used @ %111 and lshift  \ bit to be set
    or swap c!  \ update the map position

    1 sectors-already-used +!

  loop

  \ Set the GDOS header
  \ (positions 210-219 of the directory entry).

  \ 210: For opentype files, the number of 64K blocks in the file.
  \ 211: Tape header ID for ZX Spectrum files: 0 for BASIC, 1 for
  \ numeric arrays, 2 for string arrays and 3 for code.
  \ 212-213: File length. For opentype files, the length of the last
  \ block.
  \ 214-215: Start address.
  \ 216-217: Type specific.
  \ 218-219: Autostart line/address.

  \ XXX TODO -- finish
  tape-header-id      entry-pos @ 211 + mgtc!
  file-length         entry-pos @ 212 + mgt!
  start               entry-pos @ 214 + mgt!
  \ autostart           entry-pos @ 218 + mgt!

  tap-file @ 0= if
    \ This is what GDOS does for code files, not sure why:
    $FFFF entry-pos @ 216 + mgt!
  then

  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Copying

variable side
variable track
variable sector
variable file-pos            \ position in the current file contents
variable image-pos           \ position in the disk image
variable previous-image-pos  \ position in the disk image
variable copy-len            \ size of the copied chunk
variable start-of-file       \ flag for the first copied chunk

: save-file  ( -- )

  \ Save the current file to the disk image.

  starting-side @ side !
  starting-track @ track !
  starting-sector @ sector !
  0 file-pos !
  start-of-file on

  begin  file-pos @ file-length <  while

    track @ side @ sector @ geometry>pos
    dup previous-image-pos !  image-pos !

    \ Calculate the length of the chunk to be copied.

    start-of-file @ if

      \ Copy the file header from the directory entry.

      entry-pos @ 211 + image+ image-pos @ image+ /file-header move

      /file-header image-pos +!
      data/sector /file-header - copy-len !
      start-of-file off

    else

      file-length file-pos @ - dup data/sector 1- >
      if    drop data/sector
      then  copy-len !

    then

    \ Copy the data.

    file-pos @ file+  image-pos @ image+  copy-len @ move

    \ Update the sector.

    copy-len @ file-pos +!

    1 sector +!
    sector @ sectors/track > if
      1 sector !  1 track +!
      track @ tracks/side = if
        0 track !  1 side +!
        side @ sides/disk = abort" Disk full"
      then
    then

    \ Save the link to the next sector.

    file-pos @ file-length < if
      track @ side @ 128 * +  previous-image-pos @ 510 + mgtc!
      sector @  previous-image-pos @ 511 + mgtc!
    then

  repeat

  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Main

: image>file  ( -- )
  \ Save the disk image to a file.
  image /image image-filename$ $@ unslurp-file
  ;

: (file>image)  ( ca len +n -- )

  \ Copy a file to the disk image.

  \ ca len = filename
  \ +n = disk image position of a free directory entry

  >r  2dup type cr
  2dup s" .tap" ends? tap-file !
  2dup slurp-file file-contents 2!
  r> directory-entry save-file
  file-contents 2@ drop free throw
  ;

: file>image  ( ca len -- )
  \ Copy a file to the disk image, if there's a free directory entry.
  \ ca len = filename
  \ cr ." ------------------------------------------------" \ XXX INFORMER
  free-entry? if  (file>image)
  else  abort" Too many files for MGT format"  then
  ;

: files>image  ( -- )
  \ Copy the parameter files to the disk image.
  argc @ 2 do  i arg file>image  loop
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Usage

: command  ( -- )
  \ Print the name of this file.
  cr  s"   mkmgt.fs" type
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
  command ."  my_file.mgt myfile.bin"
  command ."  my_files myfile1.bin myfile2.txt"
  command ."  data.mgt *.dat data??.*"
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Boot

: check  ( -- )
  \ Make sure the number of parameters is 3 or more.
  argc @ 3 < if  usage bye  then
  ;
: run  ( -- )
  check get-image-filename files>image image>file
  ;

run bye

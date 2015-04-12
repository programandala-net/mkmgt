#! /usr/bin/env gforth

\ mkmgt
s" A-03-201504130035" 2constant version

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

\ mkmgt is written in Forth with Gforth 0.7.3 (by Anton Ertl, Bernd
\ Paysan et al.):
\   http://gnu.org/software/gforth

\ MGT disk image algorithms were adapted from:
\   pyz80 by Andrew Collier, version 1.2 2-Feb-2009
\   http://www.intensity.org.uk/samcoupe/pyz80.html

\ Information on the MGT filesystem was retrieved from:
\   http://scratchpad.wikia.com/wiki/MGT_filesystem

\ Information on the TAP file format was retrieved from the
\ documentation of the "Z80" ZX Spectrum emulator (1988-1999 by Gerton
\ Lunter).  XXX TODO URL

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ History

\ 2015-04-10:
\
\ Start. First working version: A-00-201504102349.
\
\ 2015-04-11:
\
\ New: Support for TAP files (only one ZX Spectrum file per TAP file).
\ Version A-01-2015041102147.
\
\ 2015-04-12:
\
\ New: Support for TAP files with several ZX Spectrum files.  Version
\ A-02-201504121302.
\
\ 2015-04-13:
\
\ New: Support for arrays in TAP files.  Version A-03-201504130035.

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ To-do

\ Support for TAP files with more than one ZX Spectrum file.

\ Add files to an existent disk image.

\ Check duplicated filenames.

\ Options:

\ --tap-filename : use the TAP filename, instead the filename
\ inside the TAP file.
\
\ --filename=NAME : change the filename of the next file.
\
\ --quiet
\
\ --version
\
\ --help

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Requirements

require string.fs \ Gforth dynamic strings

\ From the Galope library
\ (http://programandala.net/en.program.galope.html):

: string-suffix? ( ca1 len1 ca2 len2 -- wf )
  \ Check end of string:
  \ Is ca2 len2 the end of ca1 len1?
  \ ca1 len1 = long string
  \ ca2 len2 = suffix to check
  2swap dup 3 pick - /string  compare 0=
  ;

: unslurp-file  ( ca1 len1 ca2 len2 -- )
  \ Save a memory region to a file.
  \ ca1 len1 = content to write to the file
  \ ca2 len2 = filename
  w/o create-file throw >r
  r@ write-file throw
  r> close-file throw
  ;

: default-of  ( -- )
  postpone dup postpone of
  ;  immediate compile-only

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Disk image

\ ----------------------------------------------
\ Data

variable image-filename$

  2 constant sides/disk     \ sides (0..1)
 80 constant tracks/side    \ tracks (0..79)
 10 constant sectors/track  \ sectors (1..10)
512 constant bytes/sector   \ sector size
510 constant data/sector    \ actual data bytes saved into a sector
 80 constant files/disk     \ max number of directory entries (1..80)
 10 constant /filename      \ max length of DOS filenames
  9 constant /file-header   \ length of the file header
256 constant /entry         \ size of a directory entry  \ XXX not used

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
  \ Convert a directory entry
  \ to its position in the disk image.
  \ entry = 0..79 (instead of the usual range 1..80)
  dup >r 20 /      \ track
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
\ File

: +extension  ( ca1 len1 -- ca2 len2 )
  \ Add the .mgt file extension to the given filename, if missing.
  2dup s" .mgt" string-suffix? 0= if  s" .mgt" s+  then
  ;
: get-image-filename  ( -- )
  \ Get the first parameter, the disk image filename.
  1 arg  +extension image-filename$ $!
  ;
: save-image  ( -- )
  \ Save the disk image to a file.
  image /image image-filename$ $@ unslurp-file
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Input files

variable sectors/file     \ number of sectors used by the current file
variable starting-side
variable starting-track
variable starting-sector

variable input-filename$  \ filename of the current file (dinamyc string)
variable entry-pos        \ directory entry position in the disk image

: entry-pos+  ( n1 -- n2 )
  \ Convert a position in a directory entry
  \ to a position in the disk image.
  entry-pos @ +
  ;

2variable (file-contents)  \ contents of the current input file (memory zone)
variable file-contents-pos \ position in the file contents (used for TAP files)

: file-contents  ( -- ca len )
  \ Contents of the current input file.
  (file-contents) 2@ file-contents-pos @ /string
  ;

false value tap-file?  \ is the current input file a TAP file?

: filename!  ( ca len +n -- )
  \ Store a DOS filename into the disk image.
  \ ca len = filename
  \ +n = disk image position
  image+ dup >r /filename blank r> swap move
  ;

: tape-header+  ( n -- a )
  \ Convert a position in the tape header to its actual address.
  \ XXX TODO rename
  file-contents drop +
  ;

: (tape-header-id)  ( -- n )
  \ Tape header id of the current input file, that is TAP file.
  \ The tape header id can be:
  \ 0 for a BASIC program;
  \ 1 for a number array;
  \ 2 for a character array;
  \ 3 for a code file.
  \ A SCREEN$ file is regarded as a code file
  \ with start address 16384 and length 6912 decimal.
  3 tape-header+ c@
  ;
: tape-header-id  ( -- n )
  \ Tape header id of the current input file.
  \ If the current input file is not in a TAP file,
  \ it's regarded as a code file (tape header id 3).
  tap-file? if  (tape-header-id)  else  3  then
  ;
: file-type  ( -- n )
  \ DOS file type of the current input file.
  \ DOS file types:
  \   0: Erased            1: ZX BASIC    2: ZX numeric array
  \   3: ZX string array   4: ZX code     5: ZX 48K snapshot
  \   6: ZX Microdrive     7: ZX screen   8: Special
  \   9: ZX 128K snapshot 10: Opentype   11: ZX execute
  \ If the current input file is not in a TAP file,
  \ it's regarded as a code file (DOS file type 4).
  \ XXX TODO return file type 7 if code is 16384,6912
  tap-file? if  (tape-header-id) 1+  else  4  then
  ;
: dos-filename  ( -- ca len )
  \ DOS filename of the current input file.
  \ ca len = filename
  tap-file? if    4 tape-header+  /filename
             else  input-filename$ $@ basename /filename min  then
  ;
: file-length  ( -- n )
  \ Length of the current file.
  \ XXX TODO factor
  file-contents tap-file? if  drop 14 + @z80  else  nip  then
  ;
: start  ( -- n )
  \ Autostart line (for BASIC programs)
  \ or start address (for code files)
  tap-file? if  16 tape-header+ @z80  else  0  then
  ;
: start2  ( -- n )
  \ If the current file is a BASIC program,
  \ return the start of the variable area
  \ relative to the start of the program;
  \ if it's a code file, return 32768.
  \ If the current file is not a TAP file,
  \ it's regarded as a code file.
  tap-file? if  18 tape-header+ @z80 else  32768  then
  ;
: array-name-letter  ( -- b )
  \ Array name letter of the current file,
  \ that is an numeric or string array stored in a TAP file.
  \ Note: The array name letter is uppercase; bit 6 set means
  \ a numeric array, and bit 7 set means a string array.
  17 tape-header+ @z80
  ;

: tap-metadata+  ( +n1 -- +n2 )
  \ Update a position in the current file,
  \ skipping all TAP header info until the first
  \ actual data byte.
  24 +
  ;

: file+  ( +n -- a )
  \ Convert a position in the current file
  \ to its actual memory address.
  \ XXX TODO factor with tape-header+
  file-contents drop +  tap-file? if  tap-metadata+  then
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
    i entry>pos dup mgtc@
    if    11 + mgt@be sectors-already-used +!
    else  swap 0= unloop exit  then
  loop
  ;

: make-directory-entry  ( +n -- )

  \ Create a directory entry in the disk image.
  \ +n = disk image position of a free directory entry
  \ XXX TODO use entry number instead?

  entry-pos !

  \ Set the file type
  \ (position 0 of the directory entry).

  file-type 0 entry-pos+ mgtc!

  \ Store the filename
  \ (positions 1-10 of the directory entry).

  dos-filename 1 entry-pos+ filename!

  \ Calculate and store the number of sectors used by the file
  \ (positions 11-12 of the directory entry).

  \ The length of the file header (bytes 211-219 of the directory
  \ entry) is added because it must be saved also at the start of the
  \ file, for all supported file types -- BASIC programs, code files
  \ and arrays.

  file-length /file-header + 510 / 1+
  dup sectors/file !
  11 entry-pos+ mgt!be

  \ Calculate the starting side, track and sector of the file.

  sectors-already-used @ 10 / 4 + dup
  80 /    starting-side !
  80 mod  starting-track !
  sectors-already-used @ 10 mod 1+ starting-sector !

  \ Set the address of the first sector in the file
  \ (positions 13-14 of the directory entry).

  starting-track @ starting-side @ 128 and +
  13 entry-pos+ mgtc!  \ track (0-79, 128-207)
  starting-sector @ 14 entry-pos+ mgtc!  \ sector (1-10)

  \ Create the sector address map
  \ (positions 15-209 of the directory entry).

  \ The sector address map occupies 195 bytes. A bit is set in this
  \ map if the corresponding sector is allocated to the file. The lsb
  \ of byte 0 corresponds to track 4, sector 1. The msb of byte 0
  \ corresponds to track 4, sector 8. The lsb of byte 1 corresponds to
  \ track 4, sector 9. The msb of byte 1 corresponds to track 5,
  \ sector 6.

  sectors/file @  0 ?do

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

  \ The tape header id and the file lenght are common to any input
  \ file.

  tape-header-id      211 entry-pos+ mgtc!
  file-length         212 entry-pos+ mgt!

  \ The rest of the GDOS header depends on the origin of the input
  \ file (a host system file or a ZX Spectrum file inside a TAP file).

  tap-file? if

    \ The input file is a TAP file.

    \ There are four possible tape header ids:
    \ 0 for a BASIC program;
    \ 1 for a number array;
    \ 2 for a character array;
    \ 3 for a code file.
    \ A SCREEN$ file is regarded as a code file
    \ with start address 16384 and length 6912 decimal.

    tape-header-id case

      0 of \ BASIC program
        23755 214 entry-pos+ mgt!   \ start address
        start2 216 entry-pos+ mgt!  \ relative start of the variable area
        start 218 entry-pos+ mgt!   \ autostart line
      endof

      3 of \ code file
        start 214 entry-pos+ mgt!  \ start address
        0xFFFF 216 entry-pos+ mgt!
      endof

      default-of

        \ Numeric arrays (tape header id 1) and string arrays (tape
        \ header id 2) share the same treatment.

        \ For some unknown reason GDOS saves their start address into
        \ position 214 of the GDOS header, but it can be ommited
        \ because the loading address will depend on the size of the
        \ BASIC program and the existing variables. In fact that
        \ information is ommited in tape headers.

        0xFF 216 entry-pos+ mgtc!
        array-name-letter 217 entry-pos+ mgtc!
        0xFFFF 218 entry-pos+ mgtc!

      endof

    endcase

  else

    \ The input file is a host system file, regarded as a code file.
    0xFFFF 216 entry-pos+ mgt!

  then

  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Copying a file

\ The current file to be copied to the disk image can be a host system
\ file, regarded as ZX Spectrum code file, or an actual ZX Spectrum
\ file included in a TAP file.

variable side                \ current side of the disk
variable track               \ current track of the disk
variable sector              \ current sector of the disk
variable file-pos            \ position in the current file contents
variable image-pos           \ position in the disk image
variable previous-image-pos  \ previous position in the disk image
variable /piece              \ size of the copied piece
variable start-of-file       \ flag for the first copied piece

: copy-file-contents-piece  ( -- )

  \ Copy a piece (a sector) of the contents of the current file to the
  \ disk image.

  track @ side @ sector @ geometry>pos
  dup previous-image-pos !  image-pos !

  \ Calculate the length of the piece to be copied.

  start-of-file @ if

    \ Copy the GDOS file header from the directory entry.

    \ XXX TODO -- Confirm this has to be done for all file types.

    211 entry-pos+ image+ image-pos @ image+ /file-header move

    /file-header image-pos +!
    data/sector /file-header - /piece !
    start-of-file off

  else

    file-length file-pos @ - dup data/sector 1- >
    if  drop data/sector  then  /piece !

  then

  \ Copy the data.

  file-pos @ file+  image-pos @ image+  /piece @ move

  \ Update the sector.

  /piece @ file-pos +!

  1 sector +!
  sector @ sectors/track > if
    1 sector !  1 track +!
    track @ tracks/side = if
      0 track !  1 side +!
      side @ sides/disk = abort" Disk full"
    then
  then


  file-pos @ file-length < if  \ not the last piece yet

    \ Save the address of the next sector
    \ into the the last two bytes (510 and 511) of the current one.

    track @ side @ 128 * +  previous-image-pos @ 510 + mgtc!  \ track
    sector @  previous-image-pos @ 511 + mgtc!                \ sector

  then

  ;

: copy-file-contents  ( -- )

  \ Copy the contents of the current file to the disk image.

  starting-side @ side !
  starting-track @ track !
  starting-sector @ sector !
  0 file-pos !
  start-of-file on

  begin  file-pos @ file-length <  while
    copy-file-contents-piece
  repeat

  ;

: copy-file  ( -- )
  \ Copy the current input file to the disk image.  The current input
  \ file can be a host system file, regarded as a code file, or a ZX
  \ Spectrum file included in a TAP file.
  free-entry? if    make-directory-entry copy-file-contents
              else  abort" Too many files for MGT format"  then
  ;

: file-in-tap+  ( +n1 -- +n2 )
  \ Update a file position
  \ to point to the next ZX Spectrum file in the TAP file.
  tap-metadata+  \ point to the first actual data byte
  file-length +  \ skip the data
  1+             \ skip the checksum byte at the end of the TAP data block
  ;
: empty-tap-file?  ( -- f )
  \ Is the current TAP file empty?
  file-contents-pos @ file-in-tap+  \ new position
  dup file-contents-pos !           \ update the position
  (file-contents) 2@ nip =          \ end of the TAP file?
  ;
: copy-tap-file  ( -- )
  \ Copy a TAP file to the disk image.
  \ It can include one or more ZX Spectrum files.
  begin  dos-filename 2 spaces type cr  copy-file  empty-tap-file?  until
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Main

: get-input-file  ( ca len -- )
  \ Get the contents of an input file.
  \ ca len = filename
  slurp-file (file-contents) 2!  0 file-contents-pos !
  ;
: free-input-file  ( -- )
  \ Free the space used by the input file.
  (file-contents) 2@ drop free throw
  ;

: file>image  ( ca len -- )
  \ Copy an input file to the disk image.
  \ ca len = parameter filename
  2dup type cr
  2dup input-filename$ $!
  2dup get-input-file
  s" .tap" string-suffix?  dup to tap-file?
  if  copy-tap-file  else  copy-file  then  free-input-file
  ;
: files>image  ( -- )
  \ Copy the input files to the disk image.
  argc @ 2 ?do  i arg file>image  loop
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Usage

: command  ( -- )
  cr s"   mkmgt" type
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
  command ."  empty_disk.mgt"
  command ."  my_file.mgt myfile.bin"
  command ."  my_files myfile1.bin myfile2.txt"
  command ."  data.mgt *.dat data??.*"
  ;

\ \\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\\
\ Boot

: check  ( -- )
  \ Make sure the number of parameters is 3 or more.
  argc @ 2 < if  usage bye  then
  ;
: run  ( -- )
  check get-image-filename files>image save-image
  ;

run bye

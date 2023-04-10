## OSM PBF data content

OSM PBF files contain a collection of entities, subdivided into three types: Nodes, Ways and Relations. All three share some base properties:

- OSM ID (64-bit int)
- Edit history (completely ignored in this library)
- list of Tags, each tag being a pair of:
  - Key (string, sometimes split into multiple sections with a colon)
  - Value (string)

A Node also has:

- Latitude
- Longitude

A Way also has:

- a list of nodes, referenced by node ID

A Relation also has:

- list of Members, where each Member has:
  - Role (string)
  - reference to a node, way or relation by its OSM ID

## QuadroMaps data format

QuadroMaps encodes the data in such a way as to make specific types of access very fast. Specifically, it expects that we'll want to read a subset of the data bound by lat/lon coordinates, and that we will typically only be interested in a relatively small number of specific tags and tag values.

### Directory structure

A QuadroMaps map is a directory with a bunch of files:

- ways files:
  - ways.dat: node lists for each of the ways
  - ways.offsets: a list of offsets into ways.dat
- rels files:
  - rels.dat: member lists for each of the relations
  - rels.offsets: a list of offsets into rels.dat
  - rels.strings: deduplicated strings for relation roles
- OSM ID files:
  - osm_id.ways.dat
  - osm_id.rels.dat
  - osm_id.nodes.dat: is not generated because nodes lose identity (see below)
- tags files:
  - node.tag.\*.qtr, node.tag.\*.strings
  - way.tag.\*.qtr, way.tag.\*.strings
  - rel.tag.\*.qtr, rel.tag.\*.strings

Only ways.dat* and rels.dat* files are strictly required. OSM IDs are not used anywhere and may be deleted if not required by the application. Any tag files that the application is not interested in may be deleted.

Some of the tags files have a hash inserted into the filename before the extension. This is inserted into any filename that has uppercase characters, to avoid filename conflicts on Windows.

### Node identity

The QuadroMaps format is extremely inefficient at the task of enumerating all tags of a particular node. In fact, the format goes all out on the assumption that applications do not need to do this: it is impossible to unambiguously find all tags for any given node. So if a query for tag A and a query for tag B each returns a node at the same coordinates, it is not possible to determine whether this was a single node in PBF or two nodes at the same coordinates. Bottom line is, nodes lose their identity in QuadroMaps format.

How often do nodes overlap in PBF files? The UK dump contains ~160 million nodes, ~6 million of which have tags. There are just 885 groups of overlapping nodes that have tags (plus 122k tagless overlapping node groups). Between a third and a half of these are total duplicates, i.e. have identical tags. There are two groups of nearly 30 nodes, all of which are just postcode tags, whose usefulness is unaffected by the QuadroMaps format. This leaves a few hundred node groups which lose their usefulness, for example a group of 3 nodes labelling two distinct trees at the exact same position.

One potential solution is to nudge such duplicate nodes by the least significant bit, which is ~1cm. This could be limited to just nodes that have tags and are not a member of any relation or a way, which would be completely safe. However it's not currently considered due to the tiny amount of data that this fixes and a lack of use cases.

### Tags files

TODO

- explain directory structure
- explain tag names and sometimes values in file names
- explain strings files

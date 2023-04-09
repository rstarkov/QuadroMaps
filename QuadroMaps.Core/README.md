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
  - ways.dat: raw node lists for each of the ways
  - ways.dat.offsets: a list of offsets into ways.dat
- rels files:
  - rels.dat:
  - rels.dat.strings:
  - rels.dat.offsets: a list of offsets into rels.dat
- OSM ID files:
  - osm_id.ways.dat
  - osm_id.rels.dat
  - osm_id.nodes.dat: is not generated because nodes lose identity (see below)
- tags files:
  - node.tag.\*.qtr, node.tag.\*.strings
  - way.tag.\*.qtr, way.tag.\*.strings
  - rel.tag.\*.qtr, rel.tag.\*.strings

Only ways.dat* and rels.dat* files are strictly required. OSM IDs are not used anywhere and may be deleted if not required by the application. Any tag files that the application is not interested in may be deleted.

### Node identity

The QuadroMaps format is extremely inefficient at the task of enumerating all tags of a particular node. In fact, the format goes all out on the assumption that applications do not need to do this, and it is impossible to unambiguously find all tags for any given node. So if a query for tag A and a query for tag B each returns a node at the same coordinates, it is not possible to determine whether this was a single node in PBF or two nodes at the same coordinates. Bottom line is, nodes lose their identity in QuadroMaps format.

### Tags files

TODO

- explain directory structure
- explain tag names and sometimes values in file names
- explain strings files

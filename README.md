# IRailStressTest
Small tool to discover the true througput of IRail.be

You will need dotnet locally.




To do IRail stresstesting, clone [the stresstest](https://github.com/anyways-open/IRailStressTest) somewhere and run `ParallellRun`. This will run 100 processess in parallel. Every process generates around 4queries/sec for around 250 secs.

Note that not every process might start if there is to little memory.

When all tests are done, a ton of `.csv` files containg query details will be generated. They should be grouped with `CreateOverview.sh`, after which they can be analized with `SecBySec` or `Analyze.sh`
